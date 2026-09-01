using System.Security.Claims;
using Calendar.Data;
using Calendar.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Calendar.Services;

public sealed record InboxItemSummary(
    Guid Id,
    Guid? TaskId,
    InboxActivityType ActivityType,
    string Message,
    string ActorName,
    DateTime CreatedAt,
    bool IsRead);

public sealed record InboxSnapshot(
    IReadOnlyList<InboxItemSummary> Items,
    int UnreadCount);

public sealed class InboxStore(
    IDbContextFactory<CalendarDbContext> dbFactory,
    AuthenticationStateProvider authenticationStateProvider)
{
    public event Action? Changed;

    public async Task<int> GetUnreadCountAsync()
    {
        var userId = await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.InboxItems.AsNoTracking()
            .CountAsync(item => item.RecipientUserId == userId && item.ReadAt == null);
    }

    public async Task<InboxSnapshot> LoadRecentAsync(int take = 40)
    {
        var userId = await GetCurrentUserIdAsync();
        take = Math.Clamp(take, 1, 100);
        await using var db = await dbFactory.CreateDbContextAsync();

        var unreadCount = await db.InboxItems.AsNoTracking()
            .CountAsync(item => item.RecipientUserId == userId && item.ReadAt == null);
        var items = await db.InboxItems.AsNoTracking()
            .Where(item => item.RecipientUserId == userId)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(take)
            .Select(item => new InboxItemSummary(
                item.Id,
                item.TaskId,
                item.ActivityType,
                item.Message,
                item.Actor!.Name,
                item.CreatedAt,
                item.ReadAt != null))
            .ToListAsync();

        return new InboxSnapshot(items, unreadCount);
    }

    public async Task<bool> MarkReadAsync(Guid inboxItemId)
    {
        var userId = await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await db.InboxItems.SingleOrDefaultAsync(candidate =>
            candidate.Id == inboxItemId && candidate.RecipientUserId == userId);
        if (item is null) return false;
        if (item.ReadAt is null)
        {
            item.ReadAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            Changed?.Invoke();
        }

        return true;
    }

    public async Task<int> MarkAllReadAsync()
    {
        var userId = await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var unread = await db.InboxItems
            .Where(item => item.RecipientUserId == userId && item.ReadAt == null)
            .ToListAsync();
        if (unread.Count == 0) return 0;

        var readAt = DateTime.UtcNow;
        foreach (var item in unread)
            item.ReadAt = readAt;
        await db.SaveChangesAsync();
        Changed?.Invoke();
        return unread.Count;
    }

    private async Task<Guid> GetCurrentUserIdAsync()
    {
        var principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        if (principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            throw new UnauthorizedAccessException("You must sign in to access the Inbox.");
        }

        return userId;
    }
}
