using System.Security.Cryptography;
using Calendar.Data;
using Calendar.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Calendar.Services;

public interface IEventInvitationService
{
    Task<InvitationInspection> InspectAsync(string token, CancellationToken cancellationToken = default);
    Task<GuestEventResult> GetGuestEventAsync(string token, CancellationToken cancellationToken = default);
    Task<InvitationClaimResult> ClaimAsync(string token, Guid userId, CancellationToken cancellationToken = default);
}

public sealed class EventInvitationService(IDbContextFactory<CalendarDbContext> dbFactory) : IEventInvitationService
{
    public async Task<InvitationInspection> InspectAsync(string token, CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(token);
        if (tokenHash is null) return new(InvitationStatus.Invalid, false);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var invitation = await db.EventInvitations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
        if (invitation is null || invitation.Status is not (EventInvitationStatus.Pending or EventInvitationStatus.Accepted))
            return new(InvitationStatus.Invalid, false);
        if (invitation.ExpiresUtc <= DateTime.UtcNow)
            return new(InvitationStatus.Expired, false);

        var accountExists = await db.Users.AnyAsync(
            user => user.NormalizedEmail == invitation.NormalizedRecipientEmail,
            cancellationToken);
        return new(InvitationStatus.Valid, accountExists);
    }

    public async Task<GuestEventResult> GetGuestEventAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(token);
        if (tokenHash is null) return new(InvitationStatus.Invalid, null);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var invitation = await db.EventInvitations.AsNoTracking()
            .Include(item => item.Event)
                .ThenInclude(calendarEvent => calendarEvent!.Owner)
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
        if (invitation is null || invitation.Event is null ||
            invitation.Status is not (EventInvitationStatus.Pending or EventInvitationStatus.Accepted))
            return new(InvitationStatus.Invalid, null);
        if (invitation.ExpiresUtc <= DateTime.UtcNow)
            return new(InvitationStatus.Expired, null);

        var calendarEvent = invitation.Event;
        return new(InvitationStatus.Valid, new GuestEventView(
            calendarEvent.Title,
            calendarEvent.Start,
            calendarEvent.End,
            calendarEvent.IsAllDay,
            calendarEvent.Description,
            calendarEvent.Owner?.Name ?? calendarEvent.Owner?.Email ?? "Unknown organizer",
            calendarEvent.MeetingUrl));
    }

    public async Task<InvitationClaimResult> ClaimAsync(
        string token,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(token);
        if (tokenHash is null) return new(InvitationClaimStatus.Invalid, null);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var invitation = await db.EventInvitations
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
        if (invitation is null || invitation.Status is not (EventInvitationStatus.Pending or EventInvitationStatus.Accepted))
            return new(InvitationClaimStatus.Invalid, null);
        if (invitation.ExpiresUtc <= DateTime.UtcNow)
            return new(InvitationClaimStatus.Expired, null);

        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null || user.NormalizedEmail != invitation.NormalizedRecipientEmail)
            return new(InvitationClaimStatus.EmailMismatch, null);
        if (invitation.ClaimedByUserId is not null && invitation.ClaimedByUserId != userId)
            return new(InvitationClaimStatus.EmailMismatch, null);

        if (!await db.EventParticipants.AnyAsync(
                participant => participant.EventId == invitation.EventId && participant.UserId == userId,
                cancellationToken))
        {
            db.EventParticipants.Add(new EventParticipant
            {
                EventId = invitation.EventId,
                UserId = userId
            });
        }

        invitation.ClaimedUtc ??= DateTime.UtcNow;
        invitation.ClaimedByUserId ??= userId;
        invitation.Status = EventInvitationStatus.Accepted;
        await db.SaveChangesAsync(cancellationToken);
        return new(InvitationClaimStatus.Success, invitation.EventId);
    }

    internal static (string Token, string Hash) CreateToken()
    {
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (token, Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))));
    }

    private static string? HashToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256) return null;
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
    }
}

public enum InvitationStatus { Valid, Invalid, Expired }
public sealed record InvitationInspection(InvitationStatus Status, bool AccountExists);
public sealed record GuestEventResult(InvitationStatus Status, GuestEventView? Event);
public sealed record GuestEventView(
    string Title,
    DateTime Start,
    DateTime End,
    bool IsAllDay,
    string Description,
    string OrganizerName,
    string MeetingUrl);
public enum InvitationClaimStatus { Success, Invalid, Expired, EmailMismatch }
public sealed record InvitationClaimResult(InvitationClaimStatus Status, Guid? EventId);
