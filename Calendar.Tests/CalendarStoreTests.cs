using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Calendar.Data;
using Calendar.Models;
using Calendar.Services;
using Calendar.Services.Email;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Calendar.Tests;

public sealed class CalendarStoreTests
{
    [Fact]
    public async Task CreateEvent_AssignsAuthenticatedUserAsOwner()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await fixture.CreateStoreAsync(fixture.Owner);

        await store.CreateAsync(NewEvent(ownerId: fixture.OtherUser.Id));

        await using var db = fixture.CreateDbContext();
        var saved = await db.Events.SingleAsync();
        Assert.Equal(fixture.Owner.Id, saved.OwnerId);
        Assert.Equal("Planning session", saved.Title);
        Assert.NotEqual(Guid.Empty, saved.Version);
    }

    [Fact]
    public async Task UpdateEvent_ByOwner_PersistsChangesAndAdvancesVersion()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        await store.CreateAsync(NewEvent());
        var original = store.Events.Single().Copy();
        original.Title = "Updated planning session";

        await store.UpdateAsync(original);

        var updated = store.Events.Single();
        Assert.Equal("Updated planning session", updated.Title);
        Assert.NotEqual(original.Version, updated.Version);
    }

    [Fact]
    public async Task UpdateEvent_ByAnotherUser_IsRejectedEvenWithForgedOwnership()
    {
        var fixture = await TestFixture.CreateAsync();
        var ownerStore = await fixture.CreateStoreAsync(fixture.Owner);
        await ownerStore.CreateAsync(NewEvent());
        var attemptedUpdate = ownerStore.Events.Single().Copy();
        attemptedUpdate.OwnerId = fixture.OtherUser.Id;
        attemptedUpdate.Title = "Unauthorized change";
        var otherStore = await fixture.CreateStoreAsync(fixture.OtherUser);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => otherStore.UpdateAsync(attemptedUpdate));

        await using var db = fixture.CreateDbContext();
        Assert.Equal("Planning session", (await db.Events.SingleAsync()).Title);
    }

    [Fact]
    public async Task DeleteEvent_RequiresOwnerAndCurrentVersion()
    {
        var fixture = await TestFixture.CreateAsync();
        var ownerStore = await fixture.CreateStoreAsync(fixture.Owner);
        await ownerStore.CreateAsync(NewEvent());
        var item = ownerStore.Events.Single();
        var otherStore = await fixture.CreateStoreAsync(fixture.OtherUser);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => otherStore.DeleteAsync(item.Id, item.Version));
        await ownerStore.DeleteAsync(item.Id, item.Version);

        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.Events.ToListAsync());
    }

    [Fact]
    public async Task InvalidEvent_IsRejectedBeforePersistence()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        var invalid = NewEvent();
        invalid.Title = new string('x', 181);
        invalid.End = invalid.Start;

        await Assert.ThrowsAsync<ValidationException>(() => store.CreateAsync(invalid));

        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.Events.ToListAsync());
    }

    [Fact]
    public async Task CreateEvent_OnPastCalendarDate_IsRejected()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        var item = NewEvent();
        item.Start = DateTime.Today.AddDays(-1).AddHours(9);
        item.End = item.Start.AddHours(1);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => store.CreateAsync(item));

        Assert.Equal(CalendarTimeRules.PastDateMessage, exception.Message);
        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.Events.ToListAsync());
    }

    [Fact]
    public async Task CreateEvent_EarlierOnToday_IsAllowed()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        var item = NewEvent();
        item.Start = DateTime.Today;
        item.End = DateTime.Today.AddHours(1);

        await store.CreateAsync(item);

        Assert.Equal(DateTime.Today, store.Events.Single().Start);
    }

    [Fact]
    public async Task CreateEvent_WithValidMeetingUrl_PersistsIt()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        var item = NewEvent();
        item.MeetingUrl = "  https://meet.google.com/abc-defg-hij  ";

        await store.CreateAsync(item);

        await using var db = fixture.CreateDbContext();
        Assert.Equal("https://meet.google.com/abc-defg-hij", (await db.Events.SingleAsync()).MeetingUrl);
        Assert.Equal("https://meet.google.com/abc-defg-hij", store.Events.Single().MeetingUrl);
    }

    [Fact]
    public async Task UpdateEvent_CanEditAndRemoveMeetingUrl()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        var item = NewEvent();
        item.MeetingUrl = "https://zoom.us/j/123456789";
        await store.CreateAsync(item);
        var edit = store.Events.Single().Copy();
        edit.MeetingUrl = string.Empty;

        await store.UpdateAsync(edit);

        await using var db = fixture.CreateDbContext();
        Assert.Empty((await db.Events.SingleAsync()).MeetingUrl);
        Assert.Empty(store.Events.Single().MeetingUrl);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://meet.example.test/room")]
    [InlineData("meet.google.com/abc-defg-hij")]
    [InlineData("https://user:password@meet.google.com/abc-defg-hij")]
    public async Task InvalidMeetingUrl_IsRejectedBeforePersistence(string meetingUrl)
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        var item = NewEvent();
        item.MeetingUrl = meetingUrl;

        await Assert.ThrowsAsync<ValidationException>(() => store.CreateAsync(item));

        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.Events.ToListAsync());
    }

    [Theory]
    [InlineData(9, 15, 10, 0)]
    [InlineData(9, 0, 10, 15)]
    [InlineData(9, 0, 9, 15)]
    public async Task EventOutsideSupportedHalfHourIntervals_IsRejected(
        int startHour,
        int startMinute,
        int endHour,
        int endMinute)
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        var invalid = NewEvent();
        invalid.Start = new DateTime(2026, 10, 20, startHour, startMinute, 0);
        invalid.End = new DateTime(2026, 10, 20, endHour, endMinute, 0);

        await Assert.ThrowsAsync<ValidationException>(() => store.CreateAsync(invalid));

        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.Events.ToListAsync());
    }

    [Fact]
    public async Task UpdateEvent_WithStaleVersion_ReportsConcurrencyConflict()
    {
        var fixture = await TestFixture.CreateAsync();
        var firstSession = await fixture.CreateStoreAsync(fixture.Owner);
        await firstSession.CreateAsync(NewEvent());
        var firstEdit = firstSession.Events.Single().Copy();
        var staleEdit = firstSession.Events.Single().Copy();
        firstEdit.Title = "First session update";
        staleEdit.Title = "Stale session update";

        await firstSession.UpdateAsync(firstEdit);
        var secondSession = await fixture.CreateStoreAsync(fixture.Owner);

        await Assert.ThrowsAsync<EventConcurrencyException>(() => secondSession.UpdateAsync(staleEdit));

        Assert.Equal("First session update", secondSession.Events.Single().Title);
    }

    [Fact]
    public async Task MoveEvent_PreservesEventId()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        await store.CreateAsync(NewEvent());
        var original = store.Events.Single();

        await store.MoveAsync(original.Id, original.Version, new DateTime(2026, 10, 21, 14, 0, 0));

        Assert.Equal(original.Id, store.Events.Single().Id);
        Assert.Equal(new DateTime(2026, 10, 21, 14, 0, 0), store.Events.Single().Start);
    }

    [Fact]
    public async Task MoveEvent_PreservesDuration()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        var item = NewEvent();
        item.End = item.Start.AddMinutes(90);
        await store.CreateAsync(item);
        var original = store.Events.Single();
        var duration = original.End - original.Start;

        await store.MoveAsync(original.Id, original.Version, new DateTime(2026, 10, 22, 16, 0, 0));

        var moved = store.Events.Single();
        Assert.Equal(duration, moved.End - moved.Start);
    }

    [Fact]
    public async Task CopyEvent_CreatesNewId()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        await store.CreateAsync(NewEvent());
        var original = store.Events.Single();

        var copyId = await store.CopyAsync(original.Id, original.Version, original.Start.AddDays(1));

        Assert.NotEqual(original.Id, copyId);
        Assert.Contains(store.Events, item => item.Id == copyId);
    }

    [Fact]
    public async Task CopyEvent_LeavesOriginalInPlace()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        await store.CreateAsync(NewEvent());
        var original = store.Events.Single();

        await store.CopyAsync(original.Id, original.Version, original.Start.AddDays(1));

        Assert.Equal(2, store.Events.Count);
        Assert.Contains(store.Events, item => item.Id == original.Id && item.Start == original.Start);
    }

    [Fact]
    public async Task CopyEvent_PreservesMeetingUrl()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        var item = NewEvent();
        item.MeetingUrl = "https://teams.microsoft.com/l/meetup-join/room";
        await store.CreateAsync(item);
        var original = store.Events.Single();

        var copyId = await store.CopyAsync(original.Id, original.Version, original.Start.AddDays(1));

        Assert.Equal(item.MeetingUrl, store.Events.Single(saved => saved.Id == copyId).MeetingUrl);
    }

    [Fact]
    public async Task CopyEvent_DoesNotCopyParticipants()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        var source = NewEvent();
        source.CollaboratorEmails.Add(fixture.OtherUser.Email);
        await store.CreateAsync(source);
        var original = store.Events.Single();

        var copyId = await store.CopyAsync(original.Id, original.Version, original.Start.AddDays(1));

        await using var db = fixture.CreateDbContext();
        Assert.Contains(await db.EventParticipants.ToListAsync(), item => item.EventId == original.Id);
        Assert.DoesNotContain(await db.EventParticipants.ToListAsync(), item => item.EventId == copyId);
    }

    [Fact]
    public async Task MoveAndCopy_ByAnotherUser_AreRejected()
    {
        var fixture = await TestFixture.CreateAsync();
        var ownerStore = await fixture.CreateStoreAsync(fixture.Owner);
        await ownerStore.CreateAsync(NewEvent());
        var original = ownerStore.Events.Single();
        var otherStore = await fixture.CreateStoreAsync(fixture.OtherUser);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            otherStore.MoveAsync(original.Id, original.Version, original.Start.AddDays(1)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            otherStore.CopyAsync(original.Id, original.Version, original.Start.AddDays(1)));
    }

    [Fact]
    public async Task MoveEvent_WithStaleVersion_IsRejected()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        await store.CreateAsync(NewEvent());
        var stale = store.Events.Single().Copy();

        await store.MoveAsync(stale.Id, stale.Version, stale.Start.AddDays(1));

        await Assert.ThrowsAsync<EventConcurrencyException>(() =>
            store.MoveAsync(stale.Id, stale.Version, stale.Start.AddDays(2)));
    }

    [Fact]
    public async Task MoveAndCopy_ToPastCalendarDate_AreRejectedWithoutChangingOriginal()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        await store.CreateAsync(NewEvent());
        var original = store.Events.Single().Copy();
        var pastTarget = DateTime.Today.AddDays(-1).AddHours(9);

        var moveException = await Assert.ThrowsAsync<ValidationException>(() =>
            store.MoveAsync(original.Id, original.Version, pastTarget));
        var copyException = await Assert.ThrowsAsync<ValidationException>(() =>
            store.CopyAsync(original.Id, original.Version, pastTarget));

        Assert.Equal(CalendarTimeRules.PastDateMessage, moveException.Message);
        Assert.Equal(CalendarTimeRules.PastDateMessage, copyException.Message);
        var remaining = Assert.Single(store.Events);
        Assert.Equal(original.Id, remaining.Id);
        Assert.Equal(original.Start, remaining.Start);
    }

    [Fact]
    public async Task HistoricalEvent_CanBeEditedWithoutChangingItsCalendarDate()
    {
        var fixture = await TestFixture.CreateAsync();
        var historical = NewEvent(fixture.Owner.Id);
        historical.Id = Guid.NewGuid();
        historical.Start = DateTime.Today.AddDays(-2).AddHours(9);
        historical.End = historical.Start.AddHours(1);
        historical.Version = Guid.NewGuid();
        await using (var db = fixture.CreateDbContext())
        {
            db.Events.Add(historical);
            await db.SaveChangesAsync();
        }
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        var edit = store.Events.Single().Copy();
        edit.Title = "Historical event notes updated";

        await store.UpdateAsync(edit);

        Assert.Equal("Historical event notes updated", store.Events.Single().Title);
        Assert.Equal(historical.Start, store.Events.Single().Start);
    }

    [Fact]
    public async Task AddingRegisteredCollaborator_SendsOneShareNotification()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        var created = NewEvent();
        created.MeetingUrl = "https://meet.google.com/abc-defg-hij";
        await store.CreateAsync(created);
        var item = store.Events.Single().Copy();
        item.CollaboratorEmails.Add(fixture.OtherUser.Email);

        await store.UpdateAsync(item);

        var notification = Assert.Single(fixture.Notifier.Notifications);
        Assert.Equal(fixture.OtherUser.Name, notification.RecipientName);
        Assert.Equal(fixture.OtherUser.Email, notification.RecipientEmail);
        Assert.Equal("Planning session", notification.EventTitle);
        Assert.Equal(item.Start, notification.Start);
        Assert.Equal(item.End, notification.End);
        Assert.Equal(fixture.Owner.Name, notification.OrganizerName);
        Assert.Contains("/invitation?token=", notification.EventUrl);
        Assert.Equal(created.MeetingUrl, notification.MeetingUrl);
        await using var db = fixture.CreateDbContext();
        var invitation = await db.EventInvitations.SingleAsync();
        Assert.Equal(EventInvitationStatus.Pending, invitation.Status);
        Assert.Equal(fixture.OtherUser.Id, invitation.ClaimedByUserId);
    }

    [Fact]
    public async Task Organizer_CanViewRecipientResponseAndComment()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        var item = NewEvent();
        item.CollaboratorEmails.Add(fixture.OtherUser.Email);
        await store.CreateAsync(item);
        var eventId = store.Events.Single().Id;
        var service = fixture.CreateInvitationService();

        var result = await service.RespondAsUserAsync(
            eventId,
            fixture.OtherUser.Id,
            EventInvitationStatus.Accepted,
            "I'll join 10 minutes later.");
        await store.ReloadAsync();

        Assert.Equal(InvitationResponseResultStatus.Success, result.Status);
        var response = Assert.Single(store.Events.Single().InvitationResponses);
        Assert.Equal(fixture.OtherUser.Name, response.RecipientName);
        Assert.Equal(EventInvitationStatus.Accepted, response.Status);
        Assert.Equal("I'll join 10 minutes later.", response.Comment);
        Assert.False(response.CanRespond);
        var acceptedSummary = EventInvitationResponseSummary.From(store.Events.Single().InvitationResponses);
        Assert.Equal((1, 0, 0), (acceptedSummary.Accepted, acceptedSummary.Declined, acceptedSummary.Pending));

        await service.RespondAsUserAsync(
            eventId,
            fixture.OtherUser.Id,
            EventInvitationStatus.Declined,
            "I can no longer attend.");
        await store.ReloadAsync();

        var changedResponse = Assert.Single(store.Events.Single().InvitationResponses);
        var declinedSummary = EventInvitationResponseSummary.From(store.Events.Single().InvitationResponses);
        Assert.Equal(EventInvitationStatus.Declined, changedResponse.Status);
        Assert.Equal("I can no longer attend.", changedResponse.Comment);
        Assert.Equal((0, 1, 0), (declinedSummary.Accepted, declinedSummary.Declined, declinedSummary.Pending));
    }

    [Fact]
    public async Task AddingUnregisteredRecipient_CreatesPendingInvitationAndNotifiesOnce()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        var item = NewEvent();
        item.CollaboratorEmails.Add("future-user@luma.test");

        await store.CreateAsync(item);
        var edit = store.Events.Single().Copy();
        edit.Title = "Edited after invitation";
        await store.UpdateAsync(edit);

        await using var db = fixture.CreateDbContext();
        var invitation = Assert.Single(await db.EventInvitations.ToListAsync());
        Assert.Equal("FUTURE-USER@LUMA.TEST", invitation.NormalizedRecipientEmail);
        Assert.Equal(fixture.Owner.Id, (await db.Events.SingleAsync()).OwnerId);
        Assert.Null(invitation.ClaimedUtc);
        Assert.True(invitation.ExpiresUtc > invitation.CreatedUtc);
        var notification = Assert.Single(fixture.Notifier.Notifications);
        Assert.Equal("future-user@luma.test", notification.RecipientEmail);
        Assert.Contains("/invitation?token=", notification.EventUrl);
    }

    [Fact]
    public async Task EditingEvent_DoesNotNotifyExistingCollaboratorAgain()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        var item = NewEvent();
        item.CollaboratorEmails.Add(fixture.OtherUser.Email);
        item.CollaboratorEmails.Add(fixture.OtherUser.Email.ToUpperInvariant());
        await store.CreateAsync(item);
        var edit = store.Events.Single().Copy();
        edit.Title = "Updated without changing collaborators";

        await store.UpdateAsync(edit);

        Assert.Single(fixture.Notifier.Notifications);
    }

    [Fact]
    public async Task NotificationFailure_DoesNotUndoEventSharing()
    {
        var fixture = await TestFixture.CreateAsync();
        fixture.Notifier.ThrowOnNotify = true;
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        var item = NewEvent();
        item.CollaboratorEmails.Add(fixture.OtherUser.Email);

        await store.CreateAsync(item);

        await using var db = fixture.CreateDbContext();
        Assert.Single(await db.EventParticipants.ToListAsync());
        Assert.Contains("event was shared", store.LastNotice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NotificationFailure_DoesNotUndoPendingInvitation()
    {
        var fixture = await TestFixture.CreateAsync();
        fixture.Notifier.ThrowOnNotify = true;
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        var item = NewEvent();
        item.CollaboratorEmails.Add("future-user@luma.test");

        await store.CreateAsync(item);

        await using var db = fixture.CreateDbContext();
        var invitation = Assert.Single(await db.EventInvitations.ToListAsync());
        Assert.Equal(EventInvitationStatus.Pending, invitation.Status);
        Assert.Contains("event was shared", store.LastNotice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TitleChange_SendsEventUpdated()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await CreateSharedEventAsync(fixture, fixture.OtherUser.Email);
        fixture.Notifier.Reset();
        var edit = store.Events.Single().Copy();
        edit.Title = "Updated planning session";

        await store.UpdateAsync(edit);

        var notification = Assert.Single(fixture.Notifier.UpdatedNotifications);
        Assert.Equal("Updated planning session", notification.EventTitle);
        Assert.Contains("Title", notification.ChangedFields);
        var notificationUri = new Uri(notification.EventUrl);
        var token = QueryHelpers.ParseQuery(notificationUri.Query)["token"].Single()
            ?? throw new InvalidOperationException("The update notification did not contain an invitation token.");
        var invitationContext = await InvitationFlow.ResolveLoginContextAsync(
            fixture.CreateInvitationService(), token, notificationUri.PathAndQuery);
        Assert.Equal("/invitation", notificationUri.AbsolutePath);
        Assert.True(invitationContext.ShowGuestOption);
        var guestEvent = await fixture.CreateInvitationService().GetGuestEventAsync(token);
        Assert.Equal("Updated planning session", guestEvent.Event?.Title);
    }

    [Fact]
    public async Task DateAndTimeChange_SendsEventUpdated()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await CreateSharedEventAsync(fixture, fixture.OtherUser.Email);
        fixture.Notifier.Reset();
        var edit = store.Events.Single().Copy();
        edit.Start = edit.Start.AddDays(1).AddHours(2);
        edit.End = edit.End.AddDays(1).AddHours(2);

        await store.UpdateAsync(edit);

        var notification = Assert.Single(fixture.Notifier.UpdatedNotifications);
        Assert.Equal(edit.Start, notification.Start);
        Assert.Contains("Date", notification.ChangedFields);
        Assert.Contains("Time", notification.ChangedFields);
    }

    [Fact]
    public async Task MeetingLinkChange_SendsEventUpdated()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await CreateSharedEventAsync(fixture, fixture.OtherUser.Email);
        fixture.Notifier.Reset();
        var edit = store.Events.Single().Copy();
        edit.MeetingUrl = "https://meet.google.com/new-room";

        await store.UpdateAsync(edit);

        var notification = Assert.Single(fixture.Notifier.UpdatedNotifications);
        Assert.Equal(edit.MeetingUrl, notification.MeetingUrl);
        Assert.Contains("Meeting link", notification.ChangedFields);
    }

    [Fact]
    public async Task SaveWithoutMeaningfulChanges_SendsNoLifecycleEmail()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await CreateSharedEventAsync(fixture, fixture.OtherUser.Email);
        fixture.Notifier.Reset();

        await store.UpdateAsync(store.Events.Single().Copy());

        Assert.Empty(fixture.Notifier.Notifications);
        Assert.Empty(fixture.Notifier.UpdatedNotifications);
        Assert.Empty(fixture.Notifier.CancelledNotifications);
    }

    [Fact]
    public async Task ColorOnlyChange_SendsNoEventUpdated()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await CreateSharedEventAsync(fixture, fixture.OtherUser.Email);
        fixture.Notifier.Reset();
        var edit = store.Events.Single().Copy();
        edit.Color = "blue";

        await store.UpdateAsync(edit);

        Assert.Empty(fixture.Notifier.UpdatedNotifications);
        Assert.Equal("blue", store.Events.Single().Color);
    }

    [Fact]
    public async Task MultipleRecipients_EachReceiveOneEventUpdated()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await CreateSharedEventAsync(
            fixture, fixture.OtherUser.Email, "future-user@luma.test", fixture.OtherUser.Email.ToUpperInvariant());
        fixture.Notifier.Reset();
        var edit = store.Events.Single().Copy();
        edit.Description = "Updated agenda";

        await store.UpdateAsync(edit);

        Assert.Equal(2, fixture.Notifier.UpdatedNotifications.Count);
        Assert.Equal(2, fixture.Notifier.UpdatedNotifications
            .Select(notification => notification.RecipientEmail.ToUpperInvariant()).Distinct().Count());
    }

    [Fact]
    public async Task NewlyAddedRecipient_GetsSharedButNotUpdatedInSameOperation()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await CreateSharedEventAsync(fixture, fixture.OtherUser.Email);
        fixture.Notifier.Reset();
        var edit = store.Events.Single().Copy();
        edit.Title = "Updated for existing recipients";
        edit.CollaboratorEmails.Add("future-user@luma.test");

        await store.UpdateAsync(edit);

        Assert.Equal("future-user@luma.test", Assert.Single(fixture.Notifier.Notifications).RecipientEmail);
        Assert.Equal(fixture.OtherUser.Email, Assert.Single(fixture.Notifier.UpdatedNotifications).RecipientEmail);
    }

    [Fact]
    public async Task Deletion_SendsEventCancelled()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await CreateSharedEventAsync(fixture, fixture.OtherUser.Email);
        fixture.Notifier.Reset();
        var item = store.Events.Single();

        await store.DeleteAsync(item.Id, item.Version);

        var notification = Assert.Single(fixture.Notifier.CancelledNotifications);
        Assert.Equal(item.Title, notification.EventTitle);
        Assert.Equal(item.Start, notification.Start);
    }

    [Fact]
    public async Task DeletionNotification_ReachesAllRelevantRecipientsOnce()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await CreateSharedEventAsync(fixture, fixture.OtherUser.Email, "future-user@luma.test");
        fixture.Notifier.Reset();
        var item = store.Events.Single();

        await store.DeleteAsync(item.Id, item.Version);

        Assert.Equal(2, fixture.Notifier.CancelledNotifications.Count);
        Assert.Equal(2, fixture.Notifier.CancelledNotifications
            .Select(notification => notification.RecipientEmail.ToUpperInvariant()).Distinct().Count());
    }

    [Fact]
    public async Task UpdateEmailFailure_DoesNotUndoSuccessfulUpdate()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await CreateSharedEventAsync(fixture, fixture.OtherUser.Email);
        fixture.Notifier.Reset();
        fixture.Notifier.ThrowOnUpdated = true;
        var edit = store.Events.Single().Copy();
        edit.Title = "Persisted despite email failure";

        await store.UpdateAsync(edit);

        await using var db = fixture.CreateDbContext();
        Assert.Equal(edit.Title, (await db.Events.SingleAsync()).Title);
        Assert.Contains("notification emails", store.LastNotice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancellationEmailFailure_DoesNotUndoSuccessfulDeletion()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = await CreateSharedEventAsync(fixture, fixture.OtherUser.Email);
        fixture.Notifier.Reset();
        fixture.Notifier.ThrowOnCancelled = true;
        var item = store.Events.Single();

        await store.DeleteAsync(item.Id, item.Version);

        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.Events.ToListAsync());
        Assert.Contains("cancellation emails", store.LastNotice, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<CalendarStore> CreateSharedEventAsync(TestFixture fixture, params string[] recipients)
    {
        var store = await fixture.CreateStoreAsync(fixture.Owner);
        var item = NewEvent();
        item.CollaboratorEmails.AddRange(recipients);
        await store.CreateAsync(item);
        return store;
    }

    private static CalendarEvent NewEvent(Guid? ownerId = null) => new()
    {
        Title = "Planning session",
        Start = new DateTime(2026, 10, 20, 9, 0, 0),
        End = new DateTime(2026, 10, 20, 10, 0, 0),
        Color = "violet",
        OwnerId = ownerId ?? Guid.Empty
    };

    private sealed class TestFixture(DbContextOptions<CalendarDbContext> options, AppUser owner, AppUser otherUser)
    {
        private readonly IInvitationAccessTokenService _invitationAccessTokens =
            new InvitationAccessTokenService(new EphemeralDataProtectionProvider());
        public AppUser Owner { get; } = owner;
        public AppUser OtherUser { get; } = otherUser;
        public RecordingEventShareNotifier Notifier { get; } = new();

        public static async Task<TestFixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<CalendarDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var owner = NewUser("owner@luma.test", "Owner");
            var otherUser = NewUser("other@luma.test", "Other");
            await using var db = new CalendarDbContext(options);
            db.Users.AddRange(owner, otherUser);
            await db.SaveChangesAsync();
            return new TestFixture(options, owner, otherUser);
        }

        public CalendarDbContext CreateDbContext() => new(options);
        public EventInvitationService CreateInvitationService() =>
            new(new TestDbContextFactory(options), _invitationAccessTokens);

        public async Task<CalendarStore> CreateStoreAsync(AppUser user)
        {
            var factory = new TestDbContextFactory(options);
            var store = new CalendarStore(
                factory,
                new TestAuthenticationStateProvider(user),
                Notifier,
                new TestEventLinkBuilder(_invitationAccessTokens),
                NullLogger<CalendarStore>.Instance);
            await store.InitializeAsync();
            return store;
        }

        private static AppUser NewUser(string email, string name) => new()
        {
            Name = name,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "not-used-in-tests"
        };
    }

    private sealed class TestDbContextFactory(DbContextOptions<CalendarDbContext> options)
        : IDbContextFactory<CalendarDbContext>
    {
        public CalendarDbContext CreateDbContext() => new(options);
    }

    private sealed class TestAuthenticationStateProvider(AppUser user) : AuthenticationStateProvider
    {
        private readonly AuthenticationState _state = new(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Email, user.Email)
        ], "Test")));

        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(_state);
    }

    private sealed class RecordingEventShareNotifier : IEventShareNotifier
    {
        public List<EventShareNotification> Notifications { get; } = [];
        public List<EventUpdatedNotification> UpdatedNotifications { get; } = [];
        public List<EventCancelledNotification> CancelledNotifications { get; } = [];
        public bool ThrowOnNotify { get; set; }
        public bool ThrowOnUpdated { get; set; }
        public bool ThrowOnCancelled { get; set; }

        public void Reset()
        {
            Notifications.Clear();
            UpdatedNotifications.Clear();
            CancelledNotifications.Clear();
            ThrowOnNotify = false;
            ThrowOnUpdated = false;
            ThrowOnCancelled = false;
        }

        public Task NotifyAsync(EventShareNotification notification, CancellationToken cancellationToken = default)
        {
            if (ThrowOnNotify)
                throw new InvalidOperationException("Simulated email failure.");

            Notifications.Add(notification);
            return Task.CompletedTask;
        }

        public Task NotifyUpdatedAsync(EventUpdatedNotification notification, CancellationToken cancellationToken = default)
        {
            if (ThrowOnUpdated)
                throw new InvalidOperationException("Simulated update email failure.");

            UpdatedNotifications.Add(notification);
            return Task.CompletedTask;
        }

        public Task NotifyCancelledAsync(EventCancelledNotification notification, CancellationToken cancellationToken = default)
        {
            if (ThrowOnCancelled)
                throw new InvalidOperationException("Simulated cancellation email failure.");

            CancelledNotifications.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class TestEventLinkBuilder(IInvitationAccessTokenService invitationAccessTokens) : IEventLinkBuilder
    {
        public string Event(Guid eventId) => $"https://luma.test/?event={eventId:D}";
        public string Invitation(string token) => $"https://luma.test/invitation?token={token}";
        public string Invitation(Guid invitationId) => Invitation(invitationAccessTokens.Create(invitationId));
    }
}
