using System.Security.Cryptography;
using System.Text;
using Calendar.Data;
using Calendar.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Calendar.Services;

public interface ITaskInvitationService
{
    Task<TaskInvitationInspection> InspectAsync(string token, CancellationToken cancellationToken = default);
    Task<TaskInvitationClaimResult> ClaimAsync(string token, Guid userId, CancellationToken cancellationToken = default);
}

public sealed class TaskInvitationService(IDbContextFactory<CalendarDbContext> dbFactory) : ITaskInvitationService
{
    public async Task<TaskInvitationInspection> InspectAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = TaskInvitationToken.Hash(token);
        if (tokenHash is null) return new(TaskInvitationAccessStatus.Invalid, false);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var invitation = await db.TaskInvitations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
        if (invitation is null || invitation.Status != TaskInvitationStatus.Pending)
            return new(TaskInvitationAccessStatus.Invalid, false);
        if (invitation.ExpiresUtc <= DateTime.UtcNow)
            return new(TaskInvitationAccessStatus.Expired, false);

        var accountExists = await db.Users.AnyAsync(
            user => user.NormalizedEmail == invitation.NormalizedRecipientEmail,
            cancellationToken);
        return new(TaskInvitationAccessStatus.Valid, accountExists);
    }

    public async Task<TaskInvitationClaimResult> ClaimAsync(
        string token,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = TaskInvitationToken.Hash(token);
        if (tokenHash is null) return new(TaskInvitationClaimStatus.Invalid, null);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var invitation = await db.TaskInvitations
            .Include(item => item.Task)
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
        if (invitation is null || invitation.Task is null || invitation.Status != TaskInvitationStatus.Pending)
            return new(TaskInvitationClaimStatus.Invalid, null);
        if (invitation.ExpiresUtc <= DateTime.UtcNow)
            return new(TaskInvitationClaimStatus.Expired, null);

        var user = await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null || user.NormalizedEmail != invitation.NormalizedRecipientEmail)
            return new(TaskInvitationClaimStatus.EmailMismatch, null);
        if (invitation.Task.AssigneeId is not null || invitation.ClaimedByUserId is not null)
            return new(TaskInvitationClaimStatus.Invalid, null);

        invitation.Task.AssigneeId = user.Id;
        invitation.Task.AssignmentStatus = TaskAssignmentStatus.Pending;
        invitation.Task.AcceptedAt = null;
        invitation.Task.Version = Guid.NewGuid();
        invitation.Status = TaskInvitationStatus.Claimed;
        invitation.ClaimedByUserId = user.Id;
        invitation.ClaimedUtc = DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(TaskInvitationClaimStatus.Invalid, null);
        }

        return new(TaskInvitationClaimStatus.Success, invitation.TaskId);
    }
}

public static class TaskInvitationToken
{
    public static (string Token, string Hash) Create()
    {
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (token, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))));
    }

    public static string? Hash(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256) return null;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}

public enum TaskInvitationAccessStatus { Valid, Invalid, Expired }
public sealed record TaskInvitationInspection(TaskInvitationAccessStatus Status, bool AccountExists);
public enum TaskInvitationClaimStatus { Success, Invalid, Expired, EmailMismatch }
public sealed record TaskInvitationClaimResult(TaskInvitationClaimStatus Status, Guid? TaskId);
