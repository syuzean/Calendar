using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Calendar.Data;
using Calendar.Models;
using Calendar.Services.Email;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Calendar.Services;

public sealed class CalendarStore(
    IDbContextFactory<CalendarDbContext> dbFactory,
    AuthenticationStateProvider authenticationStateProvider,
    IEventShareNotifier shareNotifier,
    IEventLinkBuilder eventLinkBuilder,
    ILogger<CalendarStore> logger)
{
    private readonly List<CalendarEvent> _events = [];

    public IReadOnlyList<CalendarEvent> Events => _events;
    public Guid CurrentUserId { get; private set; }
    public string CurrentUserName { get; private set; } = string.Empty;
    private string CurrentUserNormalizedEmail { get; set; } = string.Empty;
    public string? LastNotice { get; private set; }
    public bool IsReady { get; private set; }
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        var principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            throw new UnauthorizedAccessException("You must sign in to open the calendar.");

        CurrentUserId = userId;
        CurrentUserName = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Email) ?? "User";
        CurrentUserNormalizedEmail = NormalizeEmail(principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty);
        await ReloadAsync();
        IsReady = true;
        Changed?.Invoke();
    }

    public async Task ReloadAsync()
    {
        if (CurrentUserId == Guid.Empty) return;

        await using var db = await dbFactory.CreateDbContextAsync();
        var items = await db.Events
            .AsNoTracking()
            .Include(item => item.Owner)
            .Include(item => item.Participants)
                .ThenInclude(participant => participant.User)
            .Include(item => item.Invitations)
            .Where(item => item.OwnerId == CurrentUserId || item.IsPublic ||
                item.Participants.Any(participant => participant.UserId == CurrentUserId))
            .OrderBy(item => item.Start)
            .ToListAsync();

        _events.Clear();
        _events.AddRange(items.Select(ToCalendarItem));
        Changed?.Invoke();
    }

    public async Task<Guid> CreateAsync(CalendarEvent item)
    {
        LastNotice = null;
        Validate(item);

        await using var db = await dbFactory.CreateDbContextAsync();
        var entity = new CalendarEvent
        {
            Id = Guid.NewGuid(),
            OwnerId = CurrentUserId,
            Version = Guid.NewGuid()
        };
        ApplyChanges(entity, item);
        db.Events.Add(entity);

        var newlyAdded = await ReplaceParticipantsAsync(db, entity, item.CollaboratorEmails);
        await db.SaveChangesAsync();
        await NotifyNewCollaboratorsAsync(entity, newlyAdded);
        await ReloadAsync();
        return entity.Id;
    }

    public async Task UpdateAsync(CalendarEvent item)
    {
        LastNotice = null;
        Validate(item);

        await using var db = await dbFactory.CreateDbContextAsync();
        var entity = await db.Events
            .Include(calendarEvent => calendarEvent.Participants)
            .Include(calendarEvent => calendarEvent.Invitations)
            .SingleOrDefaultAsync(calendarEvent => calendarEvent.Id == item.Id)
            ?? throw new EventConcurrencyException();

        EnsureOwner(entity);
        EnsureCurrentVersion(entity, item.Version);
        ApplyChanges(entity, item);
        entity.Version = Guid.NewGuid();

        var newlyAdded = await ReplaceParticipantsAsync(db, entity, item.CollaboratorEmails);
        await SaveWithConcurrencyHandlingAsync(db);
        await NotifyNewCollaboratorsAsync(entity, newlyAdded);
        await ReloadAsync();
    }

    public async Task MoveAsync(Guid id, Guid version, DateTime targetStart)
    {
        LastNotice = null;
        await using var db = await dbFactory.CreateDbContextAsync();
        var entity = await db.Events.SingleOrDefaultAsync(item => item.Id == id)
            ?? throw new EventConcurrencyException();

        EnsureOwner(entity);
        EnsureCurrentVersion(entity, version);
        var duration = entity.End - entity.Start;
        entity.Start = targetStart;
        entity.End = targetStart.Add(duration);
        Validate(entity);
        entity.Version = Guid.NewGuid();

        await SaveWithConcurrencyHandlingAsync(db);
        await ReloadAsync();
    }

    public async Task<Guid> CopyAsync(Guid id, Guid version, DateTime targetStart)
    {
        LastNotice = null;
        await using var db = await dbFactory.CreateDbContextAsync();
        var source = await db.Events.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id)
            ?? throw new EventConcurrencyException();

        EnsureOwner(source);
        EnsureCurrentVersion(source, version);
        var copy = new CalendarEvent
        {
            Id = Guid.NewGuid(),
            Title = source.Title,
            Start = targetStart,
            End = targetStart.Add(source.End - source.Start),
            IsAllDay = source.IsAllDay,
            Description = source.Description,
            MeetingUrl = source.MeetingUrl,
            Color = source.Color,
            IsPublic = source.IsPublic,
            OwnerId = CurrentUserId,
            Version = Guid.NewGuid()
        };
        Validate(copy);
        db.Events.Add(copy);
        await db.SaveChangesAsync();
        await ReloadAsync();
        return copy.Id;
    }

    public async Task DeleteAsync(Guid id, Guid version)
    {
        LastNotice = null;
        await using var db = await dbFactory.CreateDbContextAsync();
        var entity = await db.Events.SingleOrDefaultAsync(item => item.Id == id);
        if (entity is null) throw new EventConcurrencyException();

        EnsureOwner(entity);
        EnsureCurrentVersion(entity, version);
        db.Events.Remove(entity);
        await SaveWithConcurrencyHandlingAsync(db);
        await ReloadAsync();
    }

    private async Task<List<NewShareRecipient>> ReplaceParticipantsAsync(
        CalendarDbContext db,
        CalendarEvent entity,
        IEnumerable<string> collaboratorEmails)
    {
        var requestedEmails = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var value in collaboratorEmails)
        {
            var email = value.Trim();
            if (email.Length == 0) continue;
            if (!IsValidEmail(email))
                throw new ValidationException($"Enter a valid collaborator email address: {email}.");
            var normalized = NormalizeEmail(email);
            if (normalized != CurrentUserNormalizedEmail)
                requestedEmails.TryAdd(normalized, email);
        }

        var normalizedEmails = requestedEmails.Keys.ToList();
        var matchedUsers = await db.Users
            .Where(user => normalizedEmails.Contains(user.NormalizedEmail) && user.Id != CurrentUserId)
            .Select(user => new { user.Id, user.Name, user.Email, user.NormalizedEmail })
            .ToListAsync();
        var participantIds = matchedUsers.Select(user => user.Id).ToHashSet();
        var existingParticipantIds = entity.Participants.Select(participant => participant.UserId).ToHashSet();
        var participantsToRemove = entity.Participants
            .Where(participant => !participantIds.Contains(participant.UserId))
            .ToList();
        db.EventParticipants.RemoveRange(participantsToRemove);

        var now = DateTime.UtcNow;
        var notifications = new List<NewShareRecipient>();
        foreach (var user in matchedUsers)
        {
            var invitation = entity.Invitations.SingleOrDefault(item => item.NormalizedRecipientEmail == user.NormalizedEmail);
            var alreadyInvited = invitation is { Status: EventInvitationStatus.Pending } && invitation.ExpiresUtc > now;
            if (!existingParticipantIds.Contains(user.Id))
            {
                entity.Participants.Add(new EventParticipant { EventId = entity.Id, UserId = user.Id });
                if (!alreadyInvited)
                    notifications.Add(new(user.Name, user.Email, null));
            }
            if (invitation is not null)
            {
                invitation.Status = EventInvitationStatus.Accepted;
                invitation.ClaimedUtc ??= now;
                invitation.ClaimedByUserId ??= user.Id;
            }
        }

        var matchedEmails = matchedUsers.Select(user => user.NormalizedEmail).ToHashSet(StringComparer.Ordinal);
        foreach (var normalizedEmail in normalizedEmails.Where(email => !matchedEmails.Contains(email)))
        {
            var invitation = entity.Invitations.SingleOrDefault(item => item.NormalizedRecipientEmail == normalizedEmail);
            if (invitation is { Status: EventInvitationStatus.Pending } && invitation.ExpiresUtc > now)
                continue;

            var (token, tokenHash) = EventInvitationService.CreateToken();
            if (invitation is null)
            {
                invitation = new EventInvitation
                {
                    EventId = entity.Id,
                    RecipientEmail = requestedEmails[normalizedEmail],
                    NormalizedRecipientEmail = normalizedEmail
                };
                entity.Invitations.Add(invitation);
            }
            invitation.RecipientEmail = requestedEmails[normalizedEmail];
            invitation.Status = EventInvitationStatus.Pending;
            invitation.TokenHash = tokenHash;
            invitation.CreatedUtc = now;
            invitation.ExpiresUtc = now.AddDays(14);
            invitation.ClaimedUtc = null;
            invitation.ClaimedByUserId = null;
            notifications.Add(new(string.Empty, invitation.RecipientEmail, token));
        }

        foreach (var invitation in entity.Invitations.Where(item => !requestedEmails.ContainsKey(item.NormalizedRecipientEmail)))
        {
            invitation.Status = EventInvitationStatus.Revoked;
            invitation.ClaimedUtc ??= now;
        }

        return notifications;
    }

    private async Task NotifyNewCollaboratorsAsync(CalendarEvent entity, IEnumerable<NewShareRecipient> recipients)
    {
        foreach (var recipient in recipients)
        {
            try
            {
                var eventUrl = recipient.InvitationToken is null
                    ? eventLinkBuilder.Event(entity.Id)
                    : eventLinkBuilder.Invitation(recipient.InvitationToken);
                await shareNotifier.NotifyAsync(new EventShareNotification(
                    recipient.Name,
                    recipient.Email,
                    entity.Title,
                    entity.Start,
                    entity.End,
                    entity.IsAllDay,
                    CurrentUserName,
                    entity.Description,
                    entity.Color,
                    eventUrl,
                    entity.MeetingUrl));
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "An event share notification could not be sent for event {EventId}.", entity.Id);
                LastNotice = "The event was shared, but its notification email could not be sent.";
            }
        }
    }

    private void EnsureOwner(CalendarEvent entity)
    {
        if (entity.OwnerId != CurrentUserId)
            throw new UnauthorizedAccessException("Only the organizer can edit this event.");
    }

    private static void EnsureCurrentVersion(CalendarEvent entity, Guid version)
    {
        if (entity.Version != version)
            throw new EventConcurrencyException();
    }

    private static async Task SaveWithConcurrencyHandlingAsync(CalendarDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new EventConcurrencyException(exception);
        }
    }

    private static void ApplyChanges(CalendarEvent entity, CalendarEvent item)
    {
        entity.Title = item.Title.Trim();
        entity.Start = item.Start;
        entity.End = item.End;
        entity.IsAllDay = item.IsAllDay;
        entity.Description = item.Description ?? string.Empty;
        entity.MeetingUrl = item.MeetingUrl?.Trim() ?? string.Empty;
        entity.Color = item.Color ?? string.Empty;
        entity.IsPublic = item.IsPublic;
    }

    private static void Validate(CalendarEvent item)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(item.Title)) errors.Add("Event title is required.");
        else if (item.Title.Trim().Length > 180) errors.Add("Event title cannot exceed 180 characters.");
        if ((item.Description?.Length ?? 0) > 4000) errors.Add("Event description cannot exceed 4000 characters.");
        if (!MeetingUrlHelper.TryNormalize(item.MeetingUrl, out _, out var meetingUrlError)) errors.Add(meetingUrlError!);
        if ((item.Color?.Length ?? 0) > 20) errors.Add("Event color cannot exceed 20 characters.");
        if (item.Start == default) errors.Add("Event start date is required.");
        if (item.End == default) errors.Add("Event end date is required.");
        if (item.End <= item.Start) errors.Add("Event end must be after its start.");
        if (item.Start != default && !IsHalfHourBoundary(item.Start))
            errors.Add("Event start must be on the hour or half hour.");
        if (item.End != default && !IsHalfHourBoundary(item.End))
            errors.Add("Event end must be on the hour or half hour.");
        if (item.End > item.Start && item.End - item.Start < TimeSpan.FromMinutes(30))
            errors.Add("Events must be at least 30 minutes long.");

        if (errors.Count > 0)
            throw new ValidationException(string.Join(" ", errors));
    }

    private CalendarEvent ToCalendarItem(CalendarEvent entity) => new()
    {
        Id = entity.Id,
        Title = entity.Title,
        Start = entity.Start,
        End = entity.End,
        IsAllDay = entity.IsAllDay,
        Description = entity.Description,
        MeetingUrl = entity.MeetingUrl,
        Color = entity.Color,
        IsPublic = entity.IsPublic,
        OwnerId = entity.OwnerId,
        Version = entity.Version,
        OwnerName = entity.Owner?.Name ?? entity.Owner?.Email ?? "Unknown organizer",
        CanEdit = entity.OwnerId == CurrentUserId,
        IsCollaborator = entity.Participants.Any(participant => participant.UserId == CurrentUserId),
        CollaboratorEmails = entity.OwnerId == CurrentUserId || entity.Participants.Any(participant => participant.UserId == CurrentUserId)
            ? entity.Participants.Where(participant => participant.User is not null).Select(participant => participant.User!.Email)
                .Concat(entity.Invitations
                    .Where(invitation => invitation.Status == EventInvitationStatus.Pending && invitation.ExpiresUtc > DateTime.UtcNow)
                    .Select(invitation => invitation.RecipientEmail))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : []
    };

    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
    private static bool IsValidEmail(string email) =>
        email.Length <= 254 && System.Net.Mail.MailAddress.TryCreate(email, out var address) &&
        address.Address.Equals(email, StringComparison.OrdinalIgnoreCase);
    private static bool IsHalfHourBoundary(DateTime value) =>
        value.Minute % 30 == 0 && value.Ticks % TimeSpan.TicksPerMinute == 0;

    private sealed record NewShareRecipient(string Name, string Email, string? InvitationToken);
}

public sealed class EventConcurrencyException : InvalidOperationException
{
    public EventConcurrencyException()
        : base("This event changed in another session. The latest version has been loaded; please try again.")
    {
    }

    public EventConcurrencyException(Exception innerException)
        : base("This event changed in another session. The latest version has been loaded; please try again.", innerException)
    {
    }
}
