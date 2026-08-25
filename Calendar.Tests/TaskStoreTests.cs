using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Calendar.Data;
using Calendar.Models;
using Calendar.Services;
using Calendar.Services.Email;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Calendar.Tests;

public sealed class TaskStoreTests
{
    [Fact]
    public async Task AuthenticatedUser_CanCreateTask()
    {
        var fixture = await TestFixture.CreateAsync();
        var before = DateTime.UtcNow;

        var id = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        await using var db = fixture.CreateDbContext();
        var saved = await db.Tasks.SingleAsync();
        Assert.Equal(id, saved.Id);
        Assert.Equal("Prepare launch notes", saved.Title);
        Assert.Equal("Include the final checklist.", saved.Description);
        Assert.InRange(saved.CreatedAt, before, DateTime.UtcNow);
    }

    [Fact]
    public async Task Creator_ComesFromAuthenticatedIdentity()
    {
        var fixture = await TestFixture.CreateAsync();

        await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        await using var db = fixture.CreateDbContext();
        Assert.Equal(fixture.Creator.Id, (await db.Tasks.SingleAsync()).CreatorId);
    }

    [Fact]
    public async Task SelectedRegisteredAssignee_IsSaved()
    {
        var fixture = await TestFixture.CreateAsync();

        await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        await using var db = fixture.CreateDbContext();
        Assert.Equal(fixture.Assignee.Id, (await db.Tasks.SingleAsync()).AssigneeId);
    }

    [Fact]
    public async Task SelfAssignment_IsSupported()
    {
        var fixture = await TestFixture.CreateAsync();

        await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Creator.Id));

        await using var db = fixture.CreateDbContext();
        var saved = await db.Tasks.SingleAsync();
        Assert.Equal(fixture.Creator.Id, saved.CreatorId);
        Assert.Equal(fixture.Creator.Id, saved.AssigneeId);
    }

    [Fact]
    public async Task NonexistentAssignee_IsRejected()
    {
        var fixture = await TestFixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(Guid.NewGuid())));

        Assert.Contains("registered LUMA user", exception.Message);
        await AssertNoTasksAsync(fixture);
    }

    [Fact]
    public async Task MissingAssignee_IsRejected()
    {
        var fixture = await TestFixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(Guid.Empty)));

        Assert.Contains("assignee is required", exception.Message);
        await AssertNoTasksAsync(fixture);
    }

    [Fact]
    public async Task EmptyTitle_IsRejected()
    {
        var fixture = await TestFixture.CreateAsync();
        var request = NewRequest(fixture.Assignee.Id) with { Title = "   " };

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.CreateStore(fixture.Creator).CreateAsync(request));

        Assert.Contains("title is required", exception.Message);
        await AssertNoTasksAsync(fixture);
    }

    [Fact]
    public async Task MissingDeadline_IsRejected()
    {
        var fixture = await TestFixture.CreateAsync();
        var request = NewRequest(fixture.Assignee.Id) with { Deadline = null };

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.CreateStore(fixture.Creator).CreateAsync(request));

        Assert.Contains("deadline is required", exception.Message);
        await AssertNoTasksAsync(fixture);
    }

    [Fact]
    public async Task PastDeadline_IsRejected()
    {
        var fixture = await TestFixture.CreateAsync();
        var request = NewRequest(fixture.Assignee.Id) with
        {
            Deadline = DateOnly.FromDateTime(DateTime.Today.AddDays(-1))
        };

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.CreateStore(fixture.Creator).CreateAsync(request));

        Assert.Contains("cannot be before today", exception.Message);
        await AssertNoTasksAsync(fixture);
    }

    [Fact]
    public async Task ValidDeadline_IsSavedAsDateOnly()
    {
        var fixture = await TestFixture.CreateAsync();
        var deadline = DateOnly.FromDateTime(DateTime.Today.AddDays(12));

        await fixture.CreateStore(fixture.Creator).CreateAsync(
            NewRequest(fixture.Assignee.Id) with { Deadline = deadline });

        await using var db = fixture.CreateDbContext();
        Assert.Equal(deadline, (await db.Tasks.SingleAsync()).Deadline);
    }

    [Fact]
    public async Task UnauthenticatedUser_CannotCreateTask()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = new TaskStore(
            new TestDbContextFactory(fixture.Options),
            new AnonymousAuthenticationStateProvider(),
            fixture.Notifier,
            new TestTaskLinkBuilder(),
            NullLogger<TaskStore>.Instance);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.CreateAsync(NewRequest(fixture.Assignee.Id)));

        await AssertNoTasksAsync(fixture);
    }

    [Fact]
    public async Task Assignee_SeesAssignedTask()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        var tasks = await fixture.CreateStore(fixture.Assignee).LoadAssignedAsync();

        var task = Assert.Single(tasks);
        Assert.Equal(taskId, task.Id);
        Assert.Equal(fixture.Creator.Name, task.CreatorName);
    }

    [Fact]
    public async Task Creator_SeesCreatedTask()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        var tasks = await fixture.CreateStore(fixture.Creator).LoadCreatedAsync();

        var task = Assert.Single(tasks);
        Assert.Equal(taskId, task.Id);
        Assert.Equal(fixture.Assignee.Name, task.AssigneeName);
    }

    [Fact]
    public async Task UnrelatedTasks_AreNotReturned()
    {
        var fixture = await TestFixture.CreateAsync();
        var creatorStore = fixture.CreateStore(fixture.Creator);
        var ownTaskId = await creatorStore.CreateAsync(NewRequest(fixture.Assignee.Id));
        var unrelatedTaskId = await fixture.CreateStore(fixture.Assignee)
            .CreateAsync(NewRequest(fixture.Unrelated.Id) with { Title = "Unrelated task" });

        var assigned = await creatorStore.LoadAssignedAsync();
        var created = await creatorStore.LoadCreatedAsync();

        Assert.Empty(assigned);
        Assert.Contains(created, task => task.Id == ownTaskId);
        Assert.DoesNotContain(created, task => task.Id == unrelatedTaskId);
    }

    [Fact]
    public async Task Creator_CanOpenTaskDetails()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));

        var details = await store.LoadDetailsAsync(taskId);

        Assert.Equal(taskId, details.Id);
        Assert.Equal(fixture.Creator.Name, details.CreatorName);
        Assert.Equal(fixture.Assignee.Name, details.AssigneeName);
        Assert.Equal("Include the final checklist.", details.Description);
    }

    [Fact]
    public async Task Assignee_CanOpenTaskDetails()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        var details = await fixture.CreateStore(fixture.Assignee).LoadDetailsAsync(taskId);

        Assert.Equal(taskId, details.Id);
        Assert.Equal("Prepare launch notes", details.Title);
    }

    [Fact]
    public async Task UnrelatedUser_CannotOpenTaskDetails()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.CreateStore(fixture.Unrelated).LoadDetailsAsync(taskId));

        Assert.Contains("do not have access", exception.Message);
    }

    [Fact]
    public async Task MissingTask_ReportsNotFound()
    {
        var fixture = await TestFixture.CreateAsync();

        await Assert.ThrowsAsync<LumaTaskNotFoundException>(() =>
            fixture.CreateStore(fixture.Creator).LoadDetailsAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task SelfAssignedTask_AppearsInBothListsAndOpens()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Creator.Id));

        var assigned = await store.LoadAssignedAsync();
        var created = await store.LoadCreatedAsync();
        var details = await store.LoadDetailsAsync(taskId);

        Assert.Equal(taskId, Assert.Single(assigned).Id);
        Assert.Equal(taskId, Assert.Single(created).Id);
        Assert.Equal(fixture.Creator.Name, details.CreatorName);
        Assert.Equal(fixture.Creator.Name, details.AssigneeName);
    }

    [Fact]
    public async Task TaskLists_AreOrderedByNearestDeadlineFirst()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var last = await store.CreateAsync(NewRequest(fixture.Assignee.Id) with
        {
            Title = "Last",
            Deadline = today.AddDays(10)
        });
        var first = await store.CreateAsync(NewRequest(fixture.Assignee.Id) with
        {
            Title = "First",
            Deadline = today.AddDays(2)
        });
        var middle = await store.CreateAsync(NewRequest(fixture.Assignee.Id) with
        {
            Title = "Middle",
            Deadline = today.AddDays(5)
        });

        var created = await store.LoadCreatedAsync();
        var assigned = await fixture.CreateStore(fixture.Assignee).LoadAssignedAsync();

        Assert.Equal([first, middle, last], created.Select(task => task.Id));
        Assert.Equal([first, middle, last], assigned.Select(task => task.Id));
    }

    [Fact]
    public async Task ExistingOverdueTask_IsStillReturnedAndCanBeOpened()
    {
        var fixture = await TestFixture.CreateAsync();
        var overdueDeadline = DateOnly.FromDateTime(DateTime.Today.AddDays(-3));
        var overdueTask = new LumaTask
        {
            Title = "Existing overdue task",
            Description = "Created before its deadline passed.",
            CreatorId = fixture.Creator.Id,
            AssigneeId = fixture.Assignee.Id,
            Deadline = overdueDeadline,
            CreatedAt = DateTime.UtcNow.AddDays(-7)
        };
        await using (var db = fixture.CreateDbContext())
        {
            db.Tasks.Add(overdueTask);
            await db.SaveChangesAsync();
        }

        var assigned = await fixture.CreateStore(fixture.Assignee).LoadAssignedAsync();
        var details = await fixture.CreateStore(fixture.Creator).LoadDetailsAsync(overdueTask.Id);

        Assert.Equal(overdueTask.Id, Assert.Single(assigned).Id);
        Assert.Equal(overdueDeadline, details.Deadline);
    }

    [Fact]
    public async Task NewTask_StartsPending()
    {
        var fixture = await TestFixture.CreateAsync();

        await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        await using var db = fixture.CreateDbContext();
        var task = await db.Tasks.SingleAsync();
        Assert.Equal(TaskAssignmentStatus.Pending, task.AssignmentStatus);
        Assert.Null(task.AcceptedAt);
        Assert.NotEqual(Guid.Empty, task.Version);
    }

    [Fact]
    public async Task Assignee_CanAcceptTask()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        var accepted = await fixture.CreateStore(fixture.Assignee).AcceptAsync(taskId);

        Assert.Equal(TaskAssignmentStatus.Accepted, accepted.AssignmentStatus);
        Assert.NotNull(accepted.AcceptedAt);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(TaskAssignmentStatus.Accepted, (await db.Tasks.SingleAsync()).AssignmentStatus);
    }

    [Fact]
    public async Task Creator_SeesAcceptedState()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        await fixture.CreateStore(fixture.Assignee).AcceptAsync(taskId);

        var creatorStore = fixture.CreateStore(fixture.Creator);
        var summary = Assert.Single(await creatorStore.LoadCreatedAsync());
        var details = await creatorStore.LoadDetailsAsync(taskId);

        Assert.Equal(TaskAssignmentStatus.Accepted, summary.AssignmentStatus);
        Assert.Equal(TaskAssignmentStatus.Accepted, details.AssignmentStatus);
        Assert.False(details.CanAccept);
    }

    [Fact]
    public async Task UnrelatedUser_CannotAcceptTask()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.CreateStore(fixture.Unrelated).AcceptAsync(taskId));

        await using var db = fixture.CreateDbContext();
        Assert.Equal(TaskAssignmentStatus.Pending, (await db.Tasks.SingleAsync()).AssignmentStatus);
    }

    [Fact]
    public async Task Creator_CannotAcceptForAnotherAssignee()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.CreateStore(fixture.Creator).AcceptAsync(taskId));

        await using var db = fixture.CreateDbContext();
        Assert.Equal(TaskAssignmentStatus.Pending, (await db.Tasks.SingleAsync()).AssignmentStatus);
    }

    [Fact]
    public async Task SelfAssignedTask_CanBeAcceptedBySameUser()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Creator.Id));

        var accepted = await store.AcceptAsync(taskId);

        Assert.Equal(TaskAssignmentStatus.Accepted, accepted.AssignmentStatus);
        Assert.True(accepted.CanAccept);
    }

    [Fact]
    public async Task AlreadyAcceptedTask_IsIdempotent()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        var store = fixture.CreateStore(fixture.Assignee);
        var first = await store.AcceptAsync(taskId);
        Guid firstVersion;
        await using (var db = fixture.CreateDbContext())
            firstVersion = (await db.Tasks.SingleAsync()).Version;

        var second = await store.AcceptAsync(taskId);

        await using var verifyDb = fixture.CreateDbContext();
        var persisted = await verifyDb.Tasks.SingleAsync();
        Assert.Equal(first.AcceptedAt, second.AcceptedAt);
        Assert.Equal(firstVersion, persisted.Version);
        Assert.Equal(TaskAssignmentStatus.Accepted, persisted.AssignmentStatus);
        Assert.Single(fixture.Notifier.AcceptedNotifications);
    }

    [Fact]
    public async Task AcceptedState_PersistsAfterStoreReload()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        await fixture.CreateStore(fixture.Assignee).AcceptAsync(taskId);

        var reloadedStore = fixture.CreateStore(fixture.Assignee);
        var summary = Assert.Single(await reloadedStore.LoadAssignedAsync());
        var details = await reloadedStore.LoadDetailsAsync(taskId);

        Assert.Equal(TaskAssignmentStatus.Accepted, summary.AssignmentStatus);
        Assert.Equal(TaskAssignmentStatus.Accepted, details.AssignmentStatus);
        Assert.NotNull(details.AcceptedAt);
    }

    [Fact]
    public async Task TaskCreation_NotifiesOnlyTaskDoer()
    {
        var fixture = await TestFixture.CreateAsync();

        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        var notification = Assert.Single(fixture.Notifier.CreatedNotifications);
        Assert.Equal("Prepare launch notes", notification.TaskTitle);
        Assert.Contains($"task={taskId:D}", notification.TaskUrl);
        var recipient = Assert.Single(notification.Recipients);
        Assert.Equal(TaskNotificationRole.Doer, recipient.Role);
        Assert.Equal(fixture.Assignee.Email, recipient.Email);
        Assert.DoesNotContain(notification.Recipients, item => item.Email == fixture.Creator.Email);
    }

    [Fact]
    public async Task TaskAcceptance_NotifiesOnlyTaskMaker()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        await fixture.CreateStore(fixture.Assignee).AcceptAsync(taskId);

        var notification = Assert.Single(fixture.Notifier.AcceptedNotifications);
        Assert.Equal("Prepare launch notes", notification.TaskTitle);
        var recipient = Assert.Single(notification.Recipients);
        Assert.Equal(TaskNotificationRole.Maker, recipient.Role);
        Assert.Equal(fixture.Creator.Email, recipient.Email);
        Assert.DoesNotContain(notification.Recipients, item => item.Email == fixture.Assignee.Email);
        Assert.NotEqual(default, notification.AcceptedAt);
    }

    [Fact]
    public async Task SelfAssignedTask_DoesNotPrepareNotificationForEitherAction()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);

        var taskId = await store.CreateAsync(NewRequest(fixture.Creator.Id));
        await store.AcceptAsync(taskId);

        Assert.Empty(fixture.Notifier.CreatedNotifications);
        Assert.Empty(fixture.Notifier.AcceptedNotifications);
    }

    [Fact]
    public async Task NotificationFailure_DoesNotUndoTaskCreation()
    {
        var fixture = await TestFixture.CreateAsync();
        fixture.Notifier.FailCreated = true;
        var store = fixture.CreateStore(fixture.Creator);

        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));

        await using var db = fixture.CreateDbContext();
        Assert.Equal(taskId, (await db.Tasks.SingleAsync()).Id);
        Assert.Contains("notification emails", store.LastNotice);
    }

    [Fact]
    public async Task NotificationFailure_DoesNotUndoTaskAcceptance()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        fixture.Notifier.FailAccepted = true;
        var store = fixture.CreateStore(fixture.Assignee);

        var accepted = await store.AcceptAsync(taskId);

        await using var db = fixture.CreateDbContext();
        Assert.Equal(TaskAssignmentStatus.Accepted, (await db.Tasks.SingleAsync()).AssignmentStatus);
        Assert.Equal(TaskAssignmentStatus.Accepted, accepted.AssignmentStatus);
        Assert.Contains("notification emails", store.LastNotice);
    }

    [Fact]
    public async Task Assignee_CanRequestDeadlineChange_WithoutChangingActualDeadline()
    {
        var fixture = await TestFixture.CreateAsync();
        var originalDeadline = NewRequest(fixture.Assignee.Id).Deadline!.Value;
        var requestedDeadline = originalDeadline.AddDays(3);
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        var details = await fixture.CreateStore(fixture.Assignee).RequestDeadlineChangeAsync(
            taskId, new(requestedDeadline, "Waiting for the vendor."));

        Assert.Equal(originalDeadline, details.Deadline);
        Assert.Equal(TaskAssignmentStatus.DeadlineChangeRequested, details.AssignmentStatus);
        Assert.Equal(requestedDeadline, details.RequestedDeadline);
        Assert.Equal("Waiting for the vendor.", details.DeadlineChangeComment);
        Assert.NotNull(details.DeadlineChangeRequestedAt);
        await using var db = fixture.CreateDbContext();
        var persisted = await db.Tasks.SingleAsync();
        Assert.Equal(originalDeadline, persisted.Deadline);
        Assert.Equal(requestedDeadline, persisted.RequestedDeadline);
    }

    [Fact]
    public async Task Maker_CanSeeActiveDeadlineChangeRequest()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        await fixture.CreateStore(fixture.Assignee).RequestDeadlineChangeAsync(taskId, NewDeadlineRequest());

        var details = await fixture.CreateStore(fixture.Creator).LoadDetailsAsync(taskId);

        Assert.Equal(TaskAssignmentStatus.DeadlineChangeRequested, details.AssignmentStatus);
        Assert.Equal(NewDeadlineRequest().ProposedDeadline, details.RequestedDeadline);
        Assert.Equal("Waiting for the vendor.", details.DeadlineChangeComment);
        Assert.True(details.CanReviewDeadlineChange);
        Assert.False(details.CanAccept);
    }

    [Fact]
    public async Task InvalidDeadlineChangeRequests_AreRejected()
    {
        var fixture = await TestFixture.CreateAsync();
        var currentDeadline = NewRequest(fixture.Assignee.Id).Deadline!.Value;
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        var store = fixture.CreateStore(fixture.Assignee);

        await Assert.ThrowsAsync<ValidationException>(() => store.RequestDeadlineChangeAsync(taskId, new(null, null)));
        await Assert.ThrowsAsync<ValidationException>(() => store.RequestDeadlineChangeAsync(
            taskId, new(DateOnly.FromDateTime(DateTime.Today.AddDays(-1)), null)));
        await Assert.ThrowsAsync<ValidationException>(() => store.RequestDeadlineChangeAsync(taskId, new(currentDeadline, null)));

        await using var db = fixture.CreateDbContext();
        Assert.Equal(TaskAssignmentStatus.Pending, (await db.Tasks.SingleAsync()).AssignmentStatus);
    }

    [Fact]
    public async Task OnlyAssignee_CanRequestDeadlineChange()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        var request = NewDeadlineRequest();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.CreateStore(fixture.Creator).RequestDeadlineChangeAsync(taskId, request));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.CreateStore(fixture.Unrelated).RequestDeadlineChangeAsync(taskId, request));
    }

    [Fact]
    public async Task OnlyOneDeadlineChangeRequest_CanBeActive()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        var store = fixture.CreateStore(fixture.Assignee);
        await store.RequestDeadlineChangeAsync(taskId, NewDeadlineRequest());

        await Assert.ThrowsAsync<ValidationException>(() =>
            store.RequestDeadlineChangeAsync(taskId, NewDeadlineRequest(12)));
    }

    [Fact]
    public async Task ActiveDeadlineRequest_PreventsDirectAcceptance()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        var store = fixture.CreateStore(fixture.Assignee);
        await store.RequestDeadlineChangeAsync(taskId, NewDeadlineRequest());

        await Assert.ThrowsAsync<ValidationException>(() => store.AcceptAsync(taskId));

        await using var db = fixture.CreateDbContext();
        Assert.Equal(TaskAssignmentStatus.DeadlineChangeRequested, (await db.Tasks.SingleAsync()).AssignmentStatus);
    }

    [Fact]
    public async Task Maker_CanApproveDeadlineChange()
    {
        var fixture = await TestFixture.CreateAsync();
        var requestedDeadline = NewDeadlineRequest().ProposedDeadline!.Value;
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        await fixture.CreateStore(fixture.Assignee).RequestDeadlineChangeAsync(taskId, NewDeadlineRequest());

        var details = await fixture.CreateStore(fixture.Creator).ApproveDeadlineChangeAsync(taskId);

        Assert.Equal(requestedDeadline, details.Deadline);
        Assert.Equal(TaskAssignmentStatus.Accepted, details.AssignmentStatus);
        Assert.NotNull(details.AcceptedAt);
        Assert.Null(details.RequestedDeadline);
        Assert.Empty(details.DeadlineChangeComment);
        Assert.Null(details.DeadlineChangeRequestedAt);
    }

    [Fact]
    public async Task Maker_CanDeclineDeadlineChange_AndDoerCanRequestAgain()
    {
        var fixture = await TestFixture.CreateAsync();
        var originalDeadline = NewRequest(fixture.Assignee.Id).Deadline!.Value;
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        await fixture.CreateStore(fixture.Assignee).RequestDeadlineChangeAsync(taskId, NewDeadlineRequest());

        var declined = await fixture.CreateStore(fixture.Creator).DeclineDeadlineChangeAsync(taskId);
        var requestedAgain = await fixture.CreateStore(fixture.Assignee).RequestDeadlineChangeAsync(taskId, NewDeadlineRequest(12));

        Assert.Equal(originalDeadline, declined.Deadline);
        Assert.Equal(TaskAssignmentStatus.Pending, declined.AssignmentStatus);
        Assert.Null(declined.AcceptedAt);
        Assert.Equal(TaskAssignmentStatus.DeadlineChangeRequested, requestedAgain.AssignmentStatus);
        Assert.Equal(NewDeadlineRequest(12).ProposedDeadline, requestedAgain.RequestedDeadline);
    }

    [Fact]
    public async Task Doer_CanAcceptOriginalDeadlineAfterRequestIsDeclined()
    {
        var fixture = await TestFixture.CreateAsync();
        var originalDeadline = NewRequest(fixture.Assignee.Id).Deadline!.Value;
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        await fixture.CreateStore(fixture.Assignee).RequestDeadlineChangeAsync(taskId, NewDeadlineRequest());
        await fixture.CreateStore(fixture.Creator).DeclineDeadlineChangeAsync(taskId);

        var accepted = await fixture.CreateStore(fixture.Assignee).AcceptAsync(taskId);

        Assert.Equal(originalDeadline, accepted.Deadline);
        Assert.Equal(TaskAssignmentStatus.Accepted, accepted.AssignmentStatus);
        Assert.NotNull(accepted.AcceptedAt);
    }

    [Fact]
    public async Task OnlyMaker_CanReviewDeadlineChange()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        await fixture.CreateStore(fixture.Assignee).RequestDeadlineChangeAsync(taskId, NewDeadlineRequest());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.CreateStore(fixture.Assignee).ApproveDeadlineChangeAsync(taskId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.CreateStore(fixture.Unrelated).DeclineDeadlineChangeAsync(taskId));
    }

    [Fact]
    public async Task DeadlineRequest_NotifiesOnlyMaker_AndDecisionNotifiesOnlyDoer()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        await fixture.CreateStore(fixture.Assignee).RequestDeadlineChangeAsync(taskId, NewDeadlineRequest());
        var requestNotification = Assert.Single(fixture.Notifier.DeadlineRequestedNotifications);
        var maker = Assert.Single(requestNotification.Recipients);
        Assert.Equal(TaskNotificationRole.Maker, maker.Role);
        Assert.Equal(fixture.Creator.Email, maker.Email);

        await fixture.CreateStore(fixture.Creator).ApproveDeadlineChangeAsync(taskId);
        var approvalNotification = Assert.Single(fixture.Notifier.DeadlineApprovedNotifications);
        var doer = Assert.Single(approvalNotification.Recipients);
        Assert.Equal(TaskNotificationRole.Doer, doer.Role);
        Assert.Equal(fixture.Assignee.Email, doer.Email);
    }

    [Fact]
    public async Task Decline_NotifiesOnlyDoer()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        await fixture.CreateStore(fixture.Assignee).RequestDeadlineChangeAsync(taskId, NewDeadlineRequest());

        await fixture.CreateStore(fixture.Creator).DeclineDeadlineChangeAsync(taskId);

        var notification = Assert.Single(fixture.Notifier.DeadlineDeclinedNotifications);
        var recipient = Assert.Single(notification.Recipients);
        Assert.Equal(TaskNotificationRole.Doer, recipient.Role);
        Assert.Equal(fixture.Assignee.Email, recipient.Email);
    }

    [Fact]
    public async Task SelfAssignedDeadlineNegotiation_SendsNoEmail()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Creator.Id));

        await store.RequestDeadlineChangeAsync(taskId, NewDeadlineRequest());
        await store.ApproveDeadlineChangeAsync(taskId);

        Assert.Empty(fixture.Notifier.DeadlineRequestedNotifications);
        Assert.Empty(fixture.Notifier.DeadlineApprovedNotifications);
    }

    [Fact]
    public async Task EmailFailure_DoesNotUndoDeadlineRequestOrApproval()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        fixture.Notifier.FailDeadlineRequested = true;
        var doerStore = fixture.CreateStore(fixture.Assignee);

        var requested = await doerStore.RequestDeadlineChangeAsync(taskId, NewDeadlineRequest());
        Assert.Equal(TaskAssignmentStatus.DeadlineChangeRequested, requested.AssignmentStatus);
        Assert.Contains("notification email", doerStore.LastNotice);

        fixture.Notifier.FailDeadlineApproved = true;
        var makerStore = fixture.CreateStore(fixture.Creator);
        var approved = await makerStore.ApproveDeadlineChangeAsync(taskId);
        Assert.Equal(TaskAssignmentStatus.Accepted, approved.AssignmentStatus);
        Assert.Equal(NewDeadlineRequest().ProposedDeadline, approved.Deadline);
        Assert.Contains("notification email", makerStore.LastNotice);
    }

    [Fact]
    public async Task EmailFailure_DoesNotUndoDeadlineRequestDecline()
    {
        var fixture = await TestFixture.CreateAsync();
        var originalDeadline = NewRequest(fixture.Assignee.Id).Deadline!.Value;
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        await fixture.CreateStore(fixture.Assignee).RequestDeadlineChangeAsync(taskId, NewDeadlineRequest());
        fixture.Notifier.FailDeadlineDeclined = true;
        var makerStore = fixture.CreateStore(fixture.Creator);

        var declined = await makerStore.DeclineDeadlineChangeAsync(taskId);

        Assert.Equal(TaskAssignmentStatus.Pending, declined.AssignmentStatus);
        Assert.Equal(originalDeadline, declined.Deadline);
        Assert.Contains("notification email", makerStore.LastNotice);
    }

    [Fact]
    public async Task Maker_CanEditTitleAndDescription()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));
        var before = await store.LoadDetailsAsync(taskId);

        var updated = await store.UpdateContentAsync(taskId, new(
            "Updated launch notes", "Add refund coverage.", before.Version));

        Assert.Equal("Updated launch notes", updated.Title);
        Assert.Equal("Add refund coverage.", updated.Description);
        await using var db = fixture.CreateDbContext();
        var saved = await db.Tasks.SingleAsync();
        Assert.Equal(updated.Title, saved.Title);
        Assert.Equal(updated.Description, saved.Description);
    }

    [Fact]
    public async Task Maker_CanEditDescriptionWithoutChangingTitle()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));
        var before = await store.LoadDetailsAsync(taskId);

        var updated = await store.UpdateContentAsync(taskId, new(
            before.Title, "A more useful description.", before.Version));

        Assert.Equal(before.Title, updated.Title);
        Assert.Equal("A more useful description.", updated.Description);
        var notification = Assert.Single(fixture.Notifier.UpdatedNotifications);
        Assert.False(notification.Changes.TitleChanged);
        Assert.True(notification.Changes.DescriptionChanged);
    }

    [Fact]
    public async Task DoerAndUnrelatedUser_CannotEditTask()
    {
        var fixture = await TestFixture.CreateAsync();
        var makerStore = fixture.CreateStore(fixture.Creator);
        var taskId = await makerStore.CreateAsync(NewRequest(fixture.Assignee.Id));
        var version = (await makerStore.LoadDetailsAsync(taskId)).Version;
        var request = new UpdateLumaTaskContentRequest("Changed", "Changed", version);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.CreateStore(fixture.Assignee).UpdateContentAsync(taskId, request));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.CreateStore(fixture.Unrelated).UpdateContentAsync(taskId, request));

        await using var db = fixture.CreateDbContext();
        Assert.Equal("Prepare launch notes", (await db.Tasks.SingleAsync()).Title);
    }

    [Fact]
    public async Task NoChangeEdit_DoesNotUpdateVersionOrNotify()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));
        var before = await store.LoadDetailsAsync(taskId);

        var result = await store.UpdateContentAsync(taskId, new(before.Title, before.Description, before.Version));

        Assert.Equal(before.Version, result.Version);
        Assert.Empty(fixture.Notifier.UpdatedNotifications);
    }

    [Fact]
    public async Task Edit_PreservesAcceptedAssignmentState()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        await fixture.CreateStore(fixture.Assignee).AcceptAsync(taskId);
        var store = fixture.CreateStore(fixture.Creator);
        var before = await store.LoadDetailsAsync(taskId);

        var updated = await store.UpdateContentAsync(taskId, new("Revised title", before.Description, before.Version));

        Assert.Equal(TaskAssignmentStatus.Accepted, updated.AssignmentStatus);
        Assert.Equal(before.AcceptedAt, updated.AcceptedAt);
        Assert.Equal(before.Deadline, updated.Deadline);
    }

    [Fact]
    public async Task Edit_PreservesActiveDeadlineRequest()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        var requested = await fixture.CreateStore(fixture.Assignee).RequestDeadlineChangeAsync(taskId, NewDeadlineRequest());
        var store = fixture.CreateStore(fixture.Creator);

        var updated = await store.UpdateContentAsync(taskId, new("Revised title", requested.Description, requested.Version));

        Assert.Equal(TaskAssignmentStatus.DeadlineChangeRequested, updated.AssignmentStatus);
        Assert.Equal(requested.RequestedDeadline, updated.RequestedDeadline);
        Assert.Equal(requested.DeadlineChangeComment, updated.DeadlineChangeComment);
        Assert.Equal(requested.DeadlineChangeRequestedAt, updated.DeadlineChangeRequestedAt);
        Assert.Equal(requested.Deadline, updated.Deadline);
    }

    [Fact]
    public async Task StaleEdit_IsRejected()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));
        var stale = await store.LoadDetailsAsync(taskId);
        await store.UpdateContentAsync(taskId, new("First update", stale.Description, stale.Version));

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            store.UpdateContentAsync(taskId, new("Stale update", stale.Description, stale.Version)));

        Assert.Contains("changed in another session", exception.Message);
    }

    [Fact]
    public async Task MakerEdit_NotifiesOnlyDoerWithChangedFields()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));
        var before = await store.LoadDetailsAsync(taskId);

        await store.UpdateContentAsync(taskId, new("Revised title", before.Description, before.Version));

        var notification = Assert.Single(fixture.Notifier.UpdatedNotifications);
        var recipient = Assert.Single(notification.Recipients);
        Assert.Equal(TaskNotificationRole.Doer, recipient.Role);
        Assert.Equal(fixture.Assignee.Email, recipient.Email);
        Assert.True(notification.Changes.TitleChanged);
        Assert.False(notification.Changes.DescriptionChanged);
    }

    [Fact]
    public async Task NewTask_StartsToDoAndPendingCannotStart()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        var store = fixture.CreateStore(fixture.Assignee);
        var task = await store.LoadDetailsAsync(taskId);

        Assert.Equal(TaskWorkStatus.ToDo, task.WorkStatus);
        await Assert.ThrowsAsync<ValidationException>(() => store.ChangeWorkStatusAsync(
            taskId, new(TaskWorkStatus.InProgress, task.Version)));
    }

    [Fact]
    public async Task AcceptedDoer_CanProgressToInProgressThenDone()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        var store = fixture.CreateStore(fixture.Assignee);
        var accepted = await store.AcceptAsync(taskId);

        var started = await store.ChangeWorkStatusAsync(taskId, new(TaskWorkStatus.InProgress, accepted.Version));
        var completed = await store.ChangeWorkStatusAsync(taskId, new(TaskWorkStatus.Done, started.Version));

        Assert.Equal(TaskWorkStatus.InProgress, started.WorkStatus);
        Assert.Equal(TaskWorkStatus.Done, completed.WorkStatus);
        Assert.Equal(TaskAssignmentStatus.Accepted, completed.AssignmentStatus);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(TaskWorkStatus.Done, (await db.Tasks.SingleAsync()).WorkStatus);
    }

    [Fact]
    public async Task MakerAndUnrelatedUser_CannotChangeWorkStatus()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        var accepted = await fixture.CreateStore(fixture.Assignee).AcceptAsync(taskId);
        var request = new ChangeTaskWorkStatusRequest(TaskWorkStatus.InProgress, accepted.Version);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.CreateStore(fixture.Creator).ChangeWorkStatusAsync(taskId, request));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.CreateStore(fixture.Unrelated).ChangeWorkStatusAsync(taskId, request));
    }

    [Fact]
    public async Task SelfAssignedTask_CanManageWorkStatusWithoutEmail()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Creator.Id));
        var accepted = await store.AcceptAsync(taskId);

        var started = await store.ChangeWorkStatusAsync(taskId, new(TaskWorkStatus.InProgress, accepted.Version));

        Assert.Equal(TaskWorkStatus.InProgress, started.WorkStatus);
        Assert.Empty(fixture.Notifier.WorkStatusNotifications);
    }

    [Fact]
    public async Task DoerStatusChange_NotifiesOnlyMaker()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        var store = fixture.CreateStore(fixture.Assignee);
        var accepted = await store.AcceptAsync(taskId);

        await store.ChangeWorkStatusAsync(taskId, new(TaskWorkStatus.InProgress, accepted.Version));

        var notification = Assert.Single(fixture.Notifier.WorkStatusNotifications);
        var recipient = Assert.Single(notification.Recipients);
        Assert.Equal(TaskNotificationRole.Maker, recipient.Role);
        Assert.Equal(fixture.Creator.Email, recipient.Email);
        Assert.Equal(TaskWorkStatus.ToDo, notification.PreviousStatus);
        Assert.Equal(TaskWorkStatus.InProgress, notification.NewStatus);
    }

    [Fact]
    public async Task EmailFailure_DoesNotUndoEditOrStatusTransition()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        fixture.Notifier.FailUpdated = true;
        var makerStore = fixture.CreateStore(fixture.Creator);
        var before = await makerStore.LoadDetailsAsync(taskId);
        var edited = await makerStore.UpdateContentAsync(taskId, new("Persisted edit", before.Description, before.Version));
        Assert.Equal("Persisted edit", edited.Title);
        Assert.Contains("notification email", makerStore.LastNotice);

        var doerStore = fixture.CreateStore(fixture.Assignee);
        var accepted = await doerStore.AcceptAsync(taskId);
        fixture.Notifier.FailWorkStatus = true;
        var started = await doerStore.ChangeWorkStatusAsync(taskId, new(TaskWorkStatus.InProgress, accepted.Version));
        Assert.Equal(TaskWorkStatus.InProgress, started.WorkStatus);
        Assert.Contains("notification email", doerStore.LastNotice);

        await using var db = fixture.CreateDbContext();
        var saved = await db.Tasks.SingleAsync();
        Assert.Equal("Persisted edit", saved.Title);
        Assert.Equal(TaskWorkStatus.InProgress, saved.WorkStatus);
    }

    [Fact]
    public async Task Maker_CanCreateCommentAndAuthorComesFromIdentity()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));

        var comment = await store.AddCommentAsync(taskId, new("  Please check the final numbers.  "));

        Assert.Equal(fixture.Creator.Id, comment.AuthorUserId);
        Assert.Equal(fixture.Creator.Name, comment.AuthorName);
        Assert.Equal("Please check the final numbers.", comment.Text);
        await using var db = fixture.CreateDbContext();
        var saved = await db.TaskComments.SingleAsync();
        Assert.Equal(fixture.Creator.Id, saved.AuthorUserId);
        Assert.Equal(comment.Id, saved.Id);
    }

    [Fact]
    public async Task Doer_CanCreateComment()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        var comment = await fixture.CreateStore(fixture.Assignee)
            .AddCommentAsync(taskId, new("I will review it today."));

        Assert.Equal(fixture.Assignee.Id, comment.AuthorUserId);
        Assert.Equal("I will review it today.", comment.Text);
    }

    [Fact]
    public async Task UnrelatedUser_CannotCreateOrReadComments()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        var store = fixture.CreateStore(fixture.Unrelated);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            store.AddCommentAsync(taskId, new("Not allowed")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.LoadCommentsAsync(taskId));

        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.TaskComments.ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyComment_IsRejected(string text)
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            store.AddCommentAsync(taskId, new(text)));

        Assert.Contains("Write a comment", exception.Message);
        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.TaskComments.ToListAsync());
    }

    [Fact]
    public async Task Comment_PersistsAndCanBeReadAfterStoreReload()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        var created = await fixture.CreateStore(fixture.Creator)
            .AddCommentAsync(taskId, new("Persist this conversation."));

        var comments = await fixture.CreateStore(fixture.Assignee).LoadCommentsAsync(taskId);

        var reloaded = Assert.Single(comments);
        Assert.Equal(created.Id, reloaded.Id);
        Assert.Equal("Persist this conversation.", reloaded.Text);
        Assert.Equal(fixture.Creator.Name, reloaded.AuthorName);
    }

    [Fact]
    public async Task MakerComment_NotifiesOnlyDoer()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));

        await store.AddCommentAsync(taskId, new("Maker comment"));

        var notification = Assert.Single(fixture.Notifier.CommentNotifications);
        Assert.Equal(TaskNotificationRole.Maker, notification.AuthorRole);
        var recipient = Assert.Single(notification.Recipients);
        Assert.Equal(TaskNotificationRole.Doer, recipient.Role);
        Assert.Equal(fixture.Assignee.Email, recipient.Email);
    }

    [Fact]
    public async Task DoerComment_NotifiesOnlyMaker()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        await fixture.CreateStore(fixture.Assignee).AddCommentAsync(taskId, new("Doer comment"));

        var notification = Assert.Single(fixture.Notifier.CommentNotifications);
        Assert.Equal(TaskNotificationRole.Doer, notification.AuthorRole);
        var recipient = Assert.Single(notification.Recipients);
        Assert.Equal(TaskNotificationRole.Maker, recipient.Role);
        Assert.Equal(fixture.Creator.Email, recipient.Email);
    }

    [Fact]
    public async Task SelfAssignedTaskComment_SendsNoEmail()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Creator.Id));

        await store.AddCommentAsync(taskId, new("Note to self"));

        Assert.Empty(fixture.Notifier.CommentNotifications);
    }

    [Fact]
    public async Task EmailFailure_DoesNotRemoveSavedComment()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));
        fixture.Notifier.FailComment = true;

        var comment = await store.AddCommentAsync(taskId, new("Saved before email."));

        Assert.Contains("comment was saved", store.LastNotice, StringComparison.OrdinalIgnoreCase);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(comment.Id, (await db.TaskComments.SingleAsync()).Id);
    }

    private static CreateLumaTaskRequest NewRequest(Guid assigneeId) => new(
        "  Prepare launch notes  ",
        "  Include the final checklist.  ",
        assigneeId,
        DateOnly.FromDateTime(DateTime.Today.AddDays(7)));

    private static RequestTaskDeadlineChange NewDeadlineRequest(int daysFromToday = 10) => new(
        DateOnly.FromDateTime(DateTime.Today.AddDays(daysFromToday)),
        "Waiting for the vendor.");

    private static async Task AssertNoTasksAsync(TestFixture fixture)
    {
        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.Tasks.ToListAsync());
    }

    private sealed class TestFixture(
        DbContextOptions<CalendarDbContext> options,
        AppUser creator,
        AppUser assignee,
        AppUser unrelated)
    {
        public DbContextOptions<CalendarDbContext> Options { get; } = options;
        public AppUser Creator { get; } = creator;
        public AppUser Assignee { get; } = assignee;
        public AppUser Unrelated { get; } = unrelated;
        public RecordingTaskNotifier Notifier { get; } = new();

        public static async Task<TestFixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<CalendarDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var creator = NewUser("creator@luma.test", "Task Creator");
            var assignee = NewUser("assignee@luma.test", "Task Assignee");
            var unrelated = NewUser("unrelated@luma.test", "Unrelated User");

            await using var db = new CalendarDbContext(options);
            db.Users.AddRange(creator, assignee, unrelated);
            await db.SaveChangesAsync();
            return new TestFixture(options, creator, assignee, unrelated);
        }

        public CalendarDbContext CreateDbContext() => new(Options);
        public TaskStore CreateStore(AppUser user) =>
            new(
                new TestDbContextFactory(Options),
                new TestAuthenticationStateProvider(user),
                Notifier,
                new TestTaskLinkBuilder(),
                NullLogger<TaskStore>.Instance);

        private static AppUser NewUser(string email, string name) => new()
        {
            Name = name,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "test"
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

    private sealed class AnonymousAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private sealed class TestTaskLinkBuilder : ITaskLinkBuilder
    {
        public string Task(Guid taskId) => $"https://luma.test/tasks?task={taskId:D}";
    }

    private sealed class RecordingTaskNotifier : ITaskNotifier
    {
        public List<TaskCreatedNotification> CreatedNotifications { get; } = [];
        public List<TaskAcceptedNotification> AcceptedNotifications { get; } = [];
        public List<TaskDeadlineChangeRequestedNotification> DeadlineRequestedNotifications { get; } = [];
        public List<TaskDeadlineChangeApprovedNotification> DeadlineApprovedNotifications { get; } = [];
        public List<TaskDeadlineChangeDeclinedNotification> DeadlineDeclinedNotifications { get; } = [];
        public List<TaskUpdatedNotification> UpdatedNotifications { get; } = [];
        public List<TaskWorkStatusChangedNotification> WorkStatusNotifications { get; } = [];
        public List<TaskCommentAddedNotification> CommentNotifications { get; } = [];
        public bool FailCreated { get; set; }
        public bool FailAccepted { get; set; }
        public bool FailDeadlineRequested { get; set; }
        public bool FailDeadlineApproved { get; set; }
        public bool FailDeadlineDeclined { get; set; }
        public bool FailUpdated { get; set; }
        public bool FailWorkStatus { get; set; }
        public bool FailComment { get; set; }

        public Task NotifyCreatedAsync(TaskCreatedNotification notification, CancellationToken cancellationToken = default)
        {
            CreatedNotifications.Add(notification);
            return FailCreated
                ? Task.FromException(new InvalidOperationException("Simulated task-created email failure."))
                : Task.CompletedTask;
        }

        public Task NotifyAcceptedAsync(TaskAcceptedNotification notification, CancellationToken cancellationToken = default)
        {
            AcceptedNotifications.Add(notification);
            return FailAccepted
                ? Task.FromException(new InvalidOperationException("Simulated task-accepted email failure."))
                : Task.CompletedTask;
        }

        public Task NotifyDeadlineChangeRequestedAsync(TaskDeadlineChangeRequestedNotification notification, CancellationToken cancellationToken = default)
        {
            DeadlineRequestedNotifications.Add(notification);
            return FailDeadlineRequested
                ? Task.FromException(new InvalidOperationException("Simulated deadline-request email failure."))
                : Task.CompletedTask;
        }

        public Task NotifyDeadlineChangeApprovedAsync(TaskDeadlineChangeApprovedNotification notification, CancellationToken cancellationToken = default)
        {
            DeadlineApprovedNotifications.Add(notification);
            return FailDeadlineApproved
                ? Task.FromException(new InvalidOperationException("Simulated deadline-approval email failure."))
                : Task.CompletedTask;
        }

        public Task NotifyDeadlineChangeDeclinedAsync(TaskDeadlineChangeDeclinedNotification notification, CancellationToken cancellationToken = default)
        {
            DeadlineDeclinedNotifications.Add(notification);
            return FailDeadlineDeclined
                ? Task.FromException(new InvalidOperationException("Simulated deadline-decline email failure."))
                : Task.CompletedTask;
        }

        public Task NotifyUpdatedAsync(TaskUpdatedNotification notification, CancellationToken cancellationToken = default)
        {
            UpdatedNotifications.Add(notification);
            return FailUpdated
                ? Task.FromException(new InvalidOperationException("Simulated task-updated email failure."))
                : Task.CompletedTask;
        }

        public Task NotifyWorkStatusChangedAsync(TaskWorkStatusChangedNotification notification, CancellationToken cancellationToken = default)
        {
            WorkStatusNotifications.Add(notification);
            return FailWorkStatus
                ? Task.FromException(new InvalidOperationException("Simulated work-status email failure."))
                : Task.CompletedTask;
        }

        public Task NotifyCommentAddedAsync(TaskCommentAddedNotification notification, CancellationToken cancellationToken = default)
        {
            CommentNotifications.Add(notification);
            return FailComment
                ? Task.FromException(new InvalidOperationException("Simulated comment email failure."))
                : Task.CompletedTask;
        }
    }
}
