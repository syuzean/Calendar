using System.Security.Claims;
using Calendar.Data;
using Calendar.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Calendar.Services;

public sealed class CalendarStore(
    IDbContextFactory<CalendarDbContext> dbFactory,
    AuthenticationStateProvider authenticationStateProvider)
{
    private readonly List<CalendarEvent> _events = [];

    public IReadOnlyList<CalendarEvent> Events => _events;
    public Guid CurrentUserId { get; private set; }
    public string CurrentUserName { get; private set; } = string.Empty;
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
            .Where(item => item.OwnerId == CurrentUserId || item.IsPublic ||
                item.Participants.Any(participant => participant.UserId == CurrentUserId))
            .OrderBy(item => item.Start)
            .ToListAsync();

        _events.Clear();
        _events.AddRange(items.Select(ToCalendarItem));
        Changed?.Invoke();
    }

    public async Task SaveAsync(CalendarEvent item)
    {
        LastNotice = null;
        await using var db = await dbFactory.CreateDbContextAsync();
        var entity = await db.Events
            .Include(calendarEvent => calendarEvent.Participants)
            .SingleOrDefaultAsync(calendarEvent => calendarEvent.Id == item.Id);

        if (entity is null)
        {
            entity = new CalendarEvent { Id = item.Id, OwnerId = CurrentUserId };
            db.Events.Add(entity);
        }
        else if (entity.OwnerId != CurrentUserId)
        {
            throw new UnauthorizedAccessException("Only the organizer can edit this event.");
        }

        entity.Title = item.Title;
        entity.Start = item.Start;
        entity.End = item.End;
        entity.IsAllDay = item.IsAllDay;
        entity.Description = item.Description;
        entity.Color = item.Color;
        entity.IsPublic = item.IsPublic;

        var normalizedEmails = item.CollaboratorEmails
            .Select(NormalizeEmail)
            .Where(email => email.Length > 0)
            .Distinct()
            .ToList();
        var matchedUsers = await db.Users
            .Where(user => normalizedEmails.Contains(user.NormalizedEmail) && user.Id != CurrentUserId)
            .Select(user => new { user.Id, user.NormalizedEmail })
            .ToListAsync();
        var participantIds = matchedUsers.Select(user => user.Id).ToList();
        var missingEmails = normalizedEmails.Except(matchedUsers.Select(user => user.NormalizedEmail)).ToList();
        if (missingEmails.Count > 0)
            LastNotice = $"Event saved. No Luma account was found for: {string.Join(", ", missingEmails.Select(email => email.ToLowerInvariant()))}.";

        var participantsToRemove = entity.Participants
            .Where(participant => !participantIds.Contains(participant.UserId))
            .ToList();
        db.EventParticipants.RemoveRange(participantsToRemove);
        var existingParticipantIds = entity.Participants.Select(participant => participant.UserId).ToHashSet();
        foreach (var participantId in participantIds.Where(id => !existingParticipantIds.Contains(id)))
            entity.Participants.Add(new EventParticipant { EventId = entity.Id, UserId = participantId });

        await db.SaveChangesAsync();
        await ReloadAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var entity = await db.Events.SingleOrDefaultAsync(item => item.Id == id);
        if (entity is null) return;
        if (entity.OwnerId != CurrentUserId)
            throw new UnauthorizedAccessException("Only the organizer can delete this event.");

        db.Events.Remove(entity);
        await db.SaveChangesAsync();
        await ReloadAsync();
    }

    private CalendarEvent ToCalendarItem(CalendarEvent entity) => new()
    {
        Id = entity.Id,
        Title = entity.Title,
        Start = entity.Start,
        End = entity.End,
        IsAllDay = entity.IsAllDay,
        Description = entity.Description,
        Color = entity.Color,
        IsPublic = entity.IsPublic,
        OwnerId = entity.OwnerId,
        OwnerName = entity.Owner?.Name ?? entity.Owner?.Email ?? "Unknown organizer",
        CanEdit = entity.OwnerId == CurrentUserId,
        IsCollaborator = entity.Participants.Any(participant => participant.UserId == CurrentUserId),
        CollaboratorEmails = entity.OwnerId == CurrentUserId || entity.Participants.Any(participant => participant.UserId == CurrentUserId)
            ? entity.Participants.Where(participant => participant.User is not null).Select(participant => participant.User!.Email).ToList()
            : []
    };

    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
}
