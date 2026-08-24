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
    Task<InvitationResponseResult> RespondAsGuestAsync(
        string token,
        EventInvitationStatus response,
        string? comment,
        CancellationToken cancellationToken = default);
    Task<InvitationResponseResult> RespondAsUserAsync(
        Guid eventId,
        Guid userId,
        EventInvitationStatus response,
        string? comment,
        CancellationToken cancellationToken = default);
}

public sealed class EventInvitationService(
    IDbContextFactory<CalendarDbContext> dbFactory,
    IInvitationAccessTokenService invitationAccessTokens) : IEventInvitationService
{
    public async Task<InvitationInspection> InspectAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var invitationId = await ResolveInvitationIdAsync(db, token, cancellationToken);
        if (invitationId is null) return new(InvitationStatus.Invalid, false);
        var invitation = await db.EventInvitations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == invitationId, cancellationToken);
        if (invitation is null || !IsActive(invitation.Status))
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
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var invitationId = await ResolveInvitationIdAsync(db, token, cancellationToken);
        if (invitationId is null) return new(InvitationStatus.Invalid, null);
        var invitation = await db.EventInvitations.AsNoTracking()
            .Include(item => item.Event)
                .ThenInclude(calendarEvent => calendarEvent!.Owner)
            .SingleOrDefaultAsync(item => item.Id == invitationId, cancellationToken);
        if (invitation is null || invitation.Event is null ||
            !IsActive(invitation.Status))
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
            calendarEvent.MeetingUrl,
            invitation.Status,
            invitation.ResponseComment,
            invitation.ResponseUtc));
    }

    public async Task<InvitationClaimResult> ClaimAsync(
        string token,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var invitationId = await ResolveInvitationIdAsync(db, token, cancellationToken);
        if (invitationId is null) return new(InvitationClaimStatus.Invalid, null);
        var invitation = await db.EventInvitations
            .SingleOrDefaultAsync(item => item.Id == invitationId, cancellationToken);
        if (invitation is null || !IsActive(invitation.Status))
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
        await db.SaveChangesAsync(cancellationToken);
        return new(InvitationClaimStatus.Success, invitation.EventId);
    }

    public async Task<InvitationResponseResult> RespondAsGuestAsync(
        string token,
        EventInvitationStatus response,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateResponse(response, comment);
        if (validation is not null) return validation;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var invitationId = await ResolveInvitationIdAsync(db, token, cancellationToken);
        if (invitationId is null) return Failure(InvitationResponseResultStatus.Invalid);
        var invitation = await db.EventInvitations
            .SingleOrDefaultAsync(item => item.Id == invitationId, cancellationToken);
        if (invitation is null || !IsActive(invitation.Status))
            return Failure(InvitationResponseResultStatus.Invalid);
        if (invitation.ExpiresUtc <= DateTime.UtcNow)
            return Failure(InvitationResponseResultStatus.Expired);

        return await SaveResponseAsync(db, invitation, response, comment, cancellationToken);
    }

    public async Task<InvitationResponseResult> RespondAsUserAsync(
        Guid eventId,
        Guid userId,
        EventInvitationStatus response,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateResponse(response, comment);
        if (validation is not null) return validation;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null) return Failure(InvitationResponseResultStatus.NotAuthorized);

        var invitation = await db.EventInvitations
            .SingleOrDefaultAsync(item => item.EventId == eventId &&
                item.NormalizedRecipientEmail == user.NormalizedEmail, cancellationToken);
        if (invitation is null || !IsActive(invitation.Status) ||
            invitation.ClaimedByUserId is not null && invitation.ClaimedByUserId != userId)
            return Failure(InvitationResponseResultStatus.NotAuthorized);

        var isParticipant = await db.EventParticipants.AnyAsync(
            participant => participant.EventId == eventId && participant.UserId == userId,
            cancellationToken);
        if (!isParticipant) return Failure(InvitationResponseResultStatus.NotAuthorized);

        invitation.ClaimedByUserId ??= userId;
        invitation.ClaimedUtc ??= DateTime.UtcNow;
        return await SaveResponseAsync(db, invitation, response, comment, cancellationToken);
    }

    private static async Task<InvitationResponseResult> SaveResponseAsync(
        CalendarDbContext db,
        EventInvitation invitation,
        EventInvitationStatus response,
        string? comment,
        CancellationToken cancellationToken)
    {
        invitation.Status = response;
        invitation.ResponseComment = comment?.Trim() ?? string.Empty;
        invitation.ResponseUtc = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure(InvitationResponseResultStatus.Conflict);
        }

        return new(
            InvitationResponseResultStatus.Success,
            invitation.Status,
            invitation.ResponseComment,
            invitation.ResponseUtc);
    }

    private static InvitationResponseResult? ValidateResponse(EventInvitationStatus response, string? comment)
    {
        if (response is not (EventInvitationStatus.Accepted or EventInvitationStatus.Declined))
            return Failure(InvitationResponseResultStatus.Invalid);
        if ((comment?.Trim().Length ?? 0) > 1000)
            return Failure(InvitationResponseResultStatus.InvalidComment);
        return null;
    }

    private static InvitationResponseResult Failure(InvitationResponseResultStatus status) =>
        new(status, null, string.Empty, null);

    private static bool IsActive(EventInvitationStatus status) =>
        status is EventInvitationStatus.Pending or EventInvitationStatus.Accepted or EventInvitationStatus.Declined;

    private async Task<Guid?> ResolveInvitationIdAsync(
        CalendarDbContext db,
        string token,
        CancellationToken cancellationToken)
    {
        if (invitationAccessTokens.TryRead(token, out var protectedInvitationId))
            return protectedInvitationId;

        var tokenHash = HashToken(token);
        if (tokenHash is null) return null;
        return await db.EventInvitations.AsNoTracking()
            .Where(invitation => invitation.TokenHash == tokenHash)
            .Select(invitation => (Guid?)invitation.Id)
            .SingleOrDefaultAsync(cancellationToken);
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
    string MeetingUrl,
    EventInvitationStatus ResponseStatus,
    string ResponseComment,
    DateTime? ResponseUtc);
public enum InvitationClaimStatus { Success, Invalid, Expired, EmailMismatch }
public sealed record InvitationClaimResult(InvitationClaimStatus Status, Guid? EventId);
public enum InvitationResponseResultStatus
{
    Success,
    Invalid,
    Expired,
    NotAuthorized,
    Conflict,
    InvalidComment
}
public sealed record InvitationResponseResult(
    InvitationResponseResultStatus Status,
    EventInvitationStatus? ResponseStatus,
    string Comment,
    DateTime? ResponseUtc);
