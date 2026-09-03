using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Calendar.Data;
using Calendar.Models;
using Calendar.Services;
using Calendar.Services.Email;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace Calendar.Tests;

public sealed class TaskStoreTests
{
    [Fact]
    public async Task ChangeLog_CreationAndSingleFieldUpdateStoreActorAndValues()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));
        var before = await store.LoadDetailsAsync(taskId);

        await store.UpdateContentAsync(taskId, new(
            before.Title, before.Description, before.Version, TaskPriority.High, before.ProjectId,
            DescriptionMentionUserIds: before.Mentions.Select(item => item.UserId).ToArray()));

        await using var db = fixture.CreateDbContext();
        var logs = await db.TaskChangeLogs.Where(log => log.TaskId == taskId).OrderBy(log => log.CreatedAt).ToListAsync();
        var created = Assert.Single(logs, log => log.ChangeType == TaskChangeType.Created);
        Assert.Equal(fixture.Creator.Id, created.ActorUserId);
        Assert.Equal("Task", created.NewValue);
        var priority = Assert.Single(logs, log => log.FieldName == "Priority");
        Assert.Equal("None", priority.OldValue);
        Assert.Equal("High", priority.NewValue);
        Assert.Equal(fixture.Creator.Id, priority.ActorUserId);
    }

    [Fact]
    public async Task ChangeLog_NoOpAndUnauthorizedUpdateCreateNoEntries()
    {
        var fixture = await TestFixture.CreateAsync();
        var makerStore = fixture.CreateStore(fixture.Creator);
        var taskId = await makerStore.CreateAsync(NewRequest(fixture.Assignee.Id));
        var before = await makerStore.LoadDetailsAsync(taskId);
        await makerStore.UpdateContentAsync(taskId, new(before.Title, before.Description, before.Version, before.Priority, before.ProjectId));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.CreateStore(fixture.Unrelated).UpdateContentAsync(taskId,
                new("Not allowed", before.Description, before.Version, before.Priority, before.ProjectId)));

        await using var db = fixture.CreateDbContext();
        Assert.Single(await db.TaskChangeLogs.Where(log => log.TaskId == taskId).ToListAsync());
    }

    [Fact]
    public async Task ChangeLog_MultipleFieldsShareMutationAndLargeMarkdownIsCompact()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));
        var before = await store.LoadDetailsAsync(taskId);
        var markdown = "## Updated\n\n" + new string('x', 2000);

        await store.UpdateContentAsync(taskId, new(
            "Updated title", markdown, before.Version, TaskPriority.Urgent, before.ProjectId));

        await using var db = fixture.CreateDbContext();
        var changes = await db.TaskChangeLogs
            .Where(log => log.TaskId == taskId && log.ChangeType == TaskChangeType.FieldChanged)
            .ToListAsync();
        Assert.Equal(3, changes.Count);
        Assert.Single(changes.Select(log => log.MutationId).Distinct());
        var description = Assert.Single(changes, log => log.FieldName == "Description");
        Assert.StartsWith("length:", description.NewValue);
        Assert.Contains(";sha256:", description.NewValue);
        Assert.DoesNotContain(markdown, description.NewValue);
    }

    [Fact]
    public async Task ChangeLog_BugMetadataChangeIsStructured()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id) with
        {
            WorkItemType = WorkItemType.Bug,
            BugCategory = BugCategory.Functional,
            BugSeverity = BugSeverity.Low,
            BugReproducibility = BugReproducibility.Sometimes
        });
        var before = await store.LoadDetailsAsync(taskId);

        await store.UpdateContentAsync(taskId, new(
            before.Title, before.Description, before.Version, before.Priority, before.ProjectId,
            BugCategory: before.BugCategory, BugSeverity: BugSeverity.Critical,
            BugReproducibility: before.BugReproducibility, FoundInVersion: before.FoundInVersion,
            BugEnvironment: before.BugEnvironment, BugDetails: before.BugDetails,
            ReproductionMarkdown: before.ReproductionMarkdown));

        await using var db = fixture.CreateDbContext();
        var severity = Assert.Single(await db.TaskChangeLogs
            .Where(log => log.TaskId == taskId && log.FieldName == "BugSeverity").ToListAsync());
        Assert.Equal("Low", severity.OldValue);
        Assert.Equal("Critical", severity.NewValue);
    }

    [Fact]
    public async Task ChangeLog_ConcurrencyFailureLeavesNoAttemptedHistory()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));
        var stale = await store.LoadDetailsAsync(taskId);
        await store.UpdateContentAsync(taskId, new("First edit", stale.Description, stale.Version, stale.Priority, stale.ProjectId));

        await Assert.ThrowsAsync<ValidationException>(() => store.UpdateContentAsync(taskId,
            new("Failed stale edit", stale.Description, stale.Version, stale.Priority, stale.ProjectId)));

        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, await db.TaskChangeLogs.CountAsync(log => log.TaskId == taskId));
        Assert.False(await db.TaskChangeLogs.AnyAsync(log => log.NewValue == "Failed stale edit"));
    }

    [Fact]
    public async Task CreateBug_PersistsMetadataAndReturnsItInDetails()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var request = NewRequest(fixture.Assignee.Id) with
        {
            Title = "Checkout crashes",
            WorkItemType = WorkItemType.Bug,
            BugCategory = BugCategory.CrashError,
            BugSeverity = BugSeverity.Critical,
            BugReproducibility = BugReproducibility.Always,
            FoundInVersion = " 2.4.1 ",
            BugEnvironment = " Web · Chrome · Windows ",
            BugDetails = new BugAdaptiveDetailsInput(
                ErrorMessage: "Application stopped.",
                ErrorDetails: "at Checkout.Submit()",
                Logs: "request-id=abc123")
        };

        var taskId = await store.CreateAsync(request);

        await using var db = fixture.CreateDbContext();
        var saved = await db.Tasks.SingleAsync(item => item.Id == taskId);
        Assert.Equal(WorkItemType.Bug, saved.WorkItemType);
        Assert.Equal(BugCategory.CrashError, saved.BugCategory);
        Assert.Equal(BugSeverity.Critical, saved.BugSeverity);
        Assert.Equal(BugReproducibility.Always, saved.BugReproducibility);
        Assert.Equal("2.4.1", saved.FoundInVersion);
        Assert.Equal("Web · Chrome · Windows", saved.BugEnvironment);

        var details = await fixture.CreateStore(fixture.Unrelated).LoadDetailsAsync(taskId);
        Assert.Equal(WorkItemType.Bug, details.WorkItemType);
        Assert.Equal(BugCategory.CrashError, details.BugCategory);
        Assert.Equal(BugSeverity.Critical, details.BugSeverity);
        Assert.Equal("2.4.1", details.FoundInVersion);
        Assert.Equal("at Checkout.Submit()", details.BugDetails!.ErrorDetails);
        Assert.Equal("request-id=abc123", details.BugDetails.Logs);
    }

    [Fact]
    public async Task CreateBug_RejectsMissingRequiredClassification()
    {
        var fixture = await TestFixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.CreateStore(fixture.Creator).CreateAsync(
                NewRequest(fixture.Assignee.Id) with { WorkItemType = WorkItemType.Bug }));

        Assert.Contains("bug category", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bug severity", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reproduces", exception.Message, StringComparison.OrdinalIgnoreCase);
        await AssertNoTasksAsync(fixture);
    }

    [Fact]
    public async Task CreateTask_RejectsBugMetadata()
    {
        var fixture = await TestFixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.CreateStore(fixture.Creator).CreateAsync(
                NewRequest(fixture.Assignee.Id) with { BugSeverity = BugSeverity.High }));

        Assert.Contains("only be saved for Bug", exception.Message, StringComparison.OrdinalIgnoreCase);
        await AssertNoTasksAsync(fixture);
    }

    [Fact]
    public async Task EditBug_UpdatesMetadataAndConcurrencyVersion()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id) with
        {
            WorkItemType = WorkItemType.Bug,
            BugCategory = BugCategory.Functional,
            BugSeverity = BugSeverity.Medium,
            BugReproducibility = BugReproducibility.Sometimes
        });
        var before = await store.LoadDetailsAsync(taskId);

        var updated = await store.UpdateContentAsync(taskId, new UpdateLumaTaskContentRequest(
            before.Title,
            before.Description,
            before.Version,
            before.Priority,
            before.ProjectId,
            DescriptionMentionUserIds: before.Mentions.Select(item => item.UserId).ToArray(),
            BugCategory: BugCategory.Regression,
            BugSeverity: BugSeverity.High,
            BugReproducibility: BugReproducibility.Always,
            FoundInVersion: "3.0.0",
            BugEnvironment: "Production"));

        Assert.Equal(BugCategory.Regression, updated.BugCategory);
        Assert.Equal(BugSeverity.High, updated.BugSeverity);
        Assert.Equal("3.0.0", updated.FoundInVersion);
        Assert.NotEqual(before.Version, updated.Version);
    }

    [Fact]
    public async Task CreateBug_PersistsAdaptiveContextOrderedStepsAndStepImage()
    {
        var fixture = await TestFixture.CreateAsync();
        var image = ImageUpload("failure.png", "image/png", PngBytes(21, 22, 23));
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id) with
        {
            WorkItemType = WorkItemType.Bug,
            BugCategory = BugCategory.Functional,
            BugSeverity = BugSeverity.High,
            BugReproducibility = BugReproducibility.Always,
            BugDetails = new BugAdaptiveDetailsInput(
                ExpectedResult: "Payment completes.",
                ObservedResult: "401 Unauthorized."),
            ReproductionSteps =
            [
                new(null, "Open **POS**", null, false),
                new(null, "Complete first payment", null, false),
                new(null, "Start second payment", null, false),
                new(null, "Click Pay", "401 Unauthorized", true, [image]),
                new(null, "Capture request ID", null, false)
            ]
        });

        var details = await fixture.CreateStore(fixture.Unrelated).LoadDetailsAsync(taskId);
        var reproductionSteps = details.ReproductionSteps!;
        Assert.Contains("1. Open **POS**", details.ReproductionMarkdown);
        Assert.Contains("![failure.png]", details.ReproductionMarkdown);
        Assert.Equal("Payment completes.", details.BugDetails!.ExpectedResult);
        Assert.Equal("401 Unauthorized.", details.BugDetails.ObservedResult);
        Assert.Equal(["Open **POS**", "Complete first payment", "Start second payment", "Click Pay", "Capture request ID"], reproductionSteps.Select(step => step.Content));
        Assert.False(reproductionSteps[0].IsPrimaryFailure);
        Assert.True(reproductionSteps[3].IsPrimaryFailure);
        Assert.Single(reproductionSteps, step => step.IsPrimaryFailure);
        var stepImage = Assert.Single(reproductionSteps[3].Images);
        Assert.Empty(details.Attachments);

        await using var db = fixture.CreateDbContext();
        Assert.Equal(reproductionSteps[3].Id,
            (await db.TaskAttachments.SingleAsync(attachment => attachment.Id == stepImage.Id)).BugReproductionStepId);
    }

    [Fact]
    public async Task EditBug_ReordersStepsAndKeepsOnePrimaryFailure()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id) with
        {
            WorkItemType = WorkItemType.Bug,
            BugCategory = BugCategory.Regression,
            BugSeverity = BugSeverity.Medium,
            BugReproducibility = BugReproducibility.Sometimes,
            BugDetails = new BugAdaptiveDetailsInput(LastKnownGoodVersion: "2.3.8", FirstBrokenVersion: "2.4.0"),
            ReproductionSteps = [new(null, "First", null, false), new(null, "Second", null, true), new(null, "Delete me", null, false)]
        });
        var before = await store.LoadDetailsAsync(taskId);
        var beforeSteps = before.ReproductionSteps!;

        var updated = await store.UpdateContentAsync(taskId, new UpdateLumaTaskContentRequest(
            before.Title, before.Description, before.Version, before.Priority, before.ProjectId,
            DescriptionMentionUserIds: before.Mentions.Select(item => item.UserId).ToArray(),
            BugCategory: before.BugCategory, BugSeverity: before.BugSeverity,
            BugReproducibility: before.BugReproducibility, FoundInVersion: before.FoundInVersion,
            BugEnvironment: before.BugEnvironment, BugDetails: before.BugDetails,
            ReproductionSteps:
            [
                new(beforeSteps[1].Id, "Second", null, false),
                new(beforeSteps[0].Id, "First", "Failure is here", true)
            ]));

        var updatedSteps = updated.ReproductionSteps!;
        Assert.Equal(["Second", "First"], updatedSteps.Select(step => step.Content));
        Assert.False(updatedSteps[0].IsPrimaryFailure);
        Assert.True(updatedSteps[1].IsPrimaryFailure);
        Assert.Equal("Failure is here", updatedSteps[1].ObservedResult);

        await using var db = fixture.CreateDbContext();
        Assert.False(await db.BugReproductionSteps.AnyAsync(step => step.Id == beforeSteps[2].Id));
    }

    [Fact]
    public async Task CreateBug_RejectsMultiplePrimaryFailureSteps()
    {
        var fixture = await TestFixture.CreateAsync();
        var request = NewRequest(fixture.Assignee.Id) with
        {
            WorkItemType = WorkItemType.Bug,
            BugCategory = BugCategory.Compatibility,
            BugSeverity = BugSeverity.Low,
            BugReproducibility = BugReproducibility.Always,
            ReproductionSteps = [new(null, "One", null, true), new(null, "Two", null, true)]
        };

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.CreateStore(fixture.Creator).CreateAsync(request));

        Assert.Contains("Only one reproduction step", exception.Message);
        await AssertNoTasksAsync(fixture);
    }

    [Fact]
    public async Task ReproductionMarkdown_IsCanonicalAndClearsRemovedFailureStep()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id) with
        {
            WorkItemType = WorkItemType.Bug,
            BugCategory = BugCategory.Functional,
            BugSeverity = BugSeverity.High,
            BugReproducibility = BugReproducibility.Always,
            ReproductionMarkdown = "1. Open **POS**\n2. Click Pay",
            ReproductionSteps = [new(null, "Open **POS**", null, false), new(null, "Click Pay", null, true)]
        });
        var before = await store.LoadDetailsAsync(taskId);
        var beforeSteps = Assert.IsAssignableFrom<IReadOnlyList<BugReproductionStepDetails>>(before.ReproductionSteps);
        Assert.Equal(["Open **POS**", "Click Pay"], beforeSteps.Select(step => step.Content));
        Assert.True(beforeSteps[1].IsPrimaryFailure);

        var reordered = await store.UpdateContentAsync(taskId, new UpdateLumaTaskContentRequest(
            before.Title, before.Description, before.Version, before.Priority, before.ProjectId,
            DescriptionMentionUserIds: before.Mentions.Select(item => item.UserId).ToArray(),
            BugCategory: before.BugCategory, BugSeverity: before.BugSeverity,
            BugReproducibility: before.BugReproducibility, FoundInVersion: before.FoundInVersion,
            BugEnvironment: before.BugEnvironment, BugDetails: before.BugDetails,
            ReproductionMarkdown: "1. Click Pay\n2. Open **POS**"));
        var reorderedSteps = Assert.IsAssignableFrom<IReadOnlyList<BugReproductionStepDetails>>(reordered.ReproductionSteps);
        Assert.Equal(["Click Pay", "Open **POS**"], reorderedSteps.Select(step => step.Content));
        Assert.True(reorderedSteps[0].IsPrimaryFailure);

        var updated = await store.UpdateContentAsync(taskId, new UpdateLumaTaskContentRequest(
            reordered.Title, reordered.Description, reordered.Version, reordered.Priority, reordered.ProjectId,
            DescriptionMentionUserIds: reordered.Mentions.Select(item => item.UserId).ToArray(),
            BugCategory: reordered.BugCategory, BugSeverity: reordered.BugSeverity,
            BugReproducibility: reordered.BugReproducibility, FoundInVersion: reordered.FoundInVersion,
            BugEnvironment: reordered.BugEnvironment, BugDetails: reordered.BugDetails,
            ReproductionMarkdown: "1. Open **POS**"));

        Assert.Equal("1. Open **POS**", updated.ReproductionMarkdown);
        var updatedSteps = Assert.IsAssignableFrom<IReadOnlyList<BugReproductionStepDetails>>(updated.ReproductionSteps);
        Assert.Single(updatedSteps);
        Assert.False(updatedSteps[0].IsPrimaryFailure);
    }

    [Fact]
    public async Task ReproductionMarkdown_ResolvesPastedImageThroughTaskAttachmentStorage()
    {
        var fixture = await TestFixture.CreateAsync();
        var token = Guid.NewGuid().ToString("N");
        var upload = ImageUpload("error.png", "image/png", PngBytes(31, 32, 33)) with { InlineToken = token };
        var pendingImage = TaskMarkdownImageSyntax.CreateMarkdown(upload.FileName, token);
        var markdown = $"1. Click Pay\n\n   {pendingImage}";

        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id) with
        {
            WorkItemType = WorkItemType.Bug,
            BugCategory = BugCategory.Functional,
            BugSeverity = BugSeverity.Critical,
            BugReproducibility = BugReproducibility.Always,
            ReproductionMarkdown = markdown,
            ReproductionSteps = [new(null, $"Click Pay\n\n   {pendingImage}", null, true, [upload])]
        });

        var details = await fixture.CreateStore(fixture.Unrelated).LoadDetailsAsync(taskId);
        var image = Assert.Single(Assert.Single(details.ReproductionSteps!).Images);
        Assert.Contains(image.Url, details.ReproductionMarkdown);
        Assert.Contains(image.Url, details.ReproductionSteps![0].Content);
        Assert.DoesNotContain("luma-task-image:", details.ReproductionMarkdown);
    }

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
    public async Task CreateTask_SavesMarkdownDescription()
    {
        var fixture = await TestFixture.CreateAsync();
        var request = NewRequest(fixture.Assignee.Id) with
        {
            Description = "  **Problem:** reports are late.  "
        };

        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(request);

        await using var db = fixture.CreateDbContext();
        var task = await db.Tasks.SingleAsync(item => item.Id == taskId);
        Assert.Equal("**Problem:** reports are late.", task.Description);
    }

    [Fact]
    public async Task CreateTask_PersistsRegisteredMentionAndCreatesInboxItem()
    {
        var fixture = await TestFixture.CreateAsync();
        var mention = TaskMentionSyntax.CreateVisibleMention(fixture.Unrelated.Name);

        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(
            NewRequest(fixture.Assignee.Id) with
            {
                Description = $"Please review {mention}.",
                DescriptionMentionUserIds = [fixture.Unrelated.Id]
            });

        await using var db = fixture.CreateDbContext();
        var saved = await db.Tasks.Include(task => task.Mentions).SingleAsync(task => task.Id == taskId);
        var reference = Assert.Single(saved.Mentions);
        Assert.Equal(fixture.Unrelated.Id, reference.UserId);
        Assert.Contains("@Unrelated User", saved.Description);
        Assert.DoesNotContain("luma-user:", saved.Description);
        Assert.DoesNotContain(fixture.Unrelated.Id.ToString(), saved.Description);
        var inboxItem = Assert.Single(await db.InboxItems
            .Where(item => item.ActivityType == InboxActivityType.TaskMentioned)
            .ToListAsync());
        AssertInbox(inboxItem, InboxActivityType.TaskMentioned, fixture.Creator, fixture.Unrelated, taskId);
    }

    [Fact]
    public async Task CreateTask_ResolvesUniqueVisibleMentionFromPastedText()
    {
        var fixture = await TestFixture.CreateAsync();

        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(
            NewRequest(fixture.Assignee.Id) with
            {
                Description = "Please check this @Unrelated User"
            });

        await using var db = fixture.CreateDbContext();
        var mention = Assert.Single(await db.TaskMentions
            .Where(item => item.TaskId == taskId)
            .ToListAsync());
        Assert.Equal(fixture.Unrelated.Id, mention.UserId);
        var notification = Assert.Single(await db.InboxItems
            .Where(item => item.TaskId == taskId && item.ActivityType == InboxActivityType.TaskMentioned)
            .ToListAsync());
        Assert.Equal(fixture.Unrelated.Id, notification.RecipientUserId);
    }

    [Fact]
    public async Task CreateTask_DoesNotResolveAmbiguousPastedMention()
    {
        var fixture = await TestFixture.CreateAsync();
        await using (var db = fixture.CreateDbContext())
        {
            db.Users.AddRange(
                TestFixture.NewUser("tigran.one@luma.test", "Tigran Hakobyan"),
                TestFixture.NewUser("tigran.two@luma.test", "Tigran Hakobyan"));
            await db.SaveChangesAsync();
        }

        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(
            NewRequest(fixture.Assignee.Id) with
            {
                Description = "Please check this @Tigran Hakobyan"
            });

        await using var verify = fixture.CreateDbContext();
        Assert.Empty(await verify.TaskMentions.Where(item => item.TaskId == taskId).ToListAsync());
        Assert.Empty(await verify.InboxItems
            .Where(item => item.TaskId == taskId && item.ActivityType == InboxActivityType.TaskMentioned)
            .ToListAsync());
        Assert.Contains("@Tigran Hakobyan", (await verify.Tasks.SingleAsync(item => item.Id == taskId)).Description);
    }

    [Fact]
    public async Task CreateTask_KeepsUnknownPastedMentionAsPlainText()
    {
        var fixture = await TestFixture.CreateAsync();

        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(
            NewRequest(fixture.Assignee.Id) with
            {
                Description = "Please check this @Person Who Does Not Exist"
            });

        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.TaskMentions.Where(item => item.TaskId == taskId).ToListAsync());
        Assert.Empty(await db.InboxItems
            .Where(item => item.TaskId == taskId && item.ActivityType == InboxActivityType.TaskMentioned)
            .ToListAsync());
        Assert.Contains(
            "@Person Who Does Not Exist",
            (await db.Tasks.SingleAsync(item => item.Id == taskId)).Description);
    }

    [Fact]
    public async Task EditTask_ResolvesUniqueVisibleMentionFromEditedText()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));
        var before = await store.LoadDetailsAsync(taskId);

        await store.UpdateContentAsync(taskId, new(
            before.Title,
            "Edited with @Unrelated User",
            before.Version));

        await using var db = fixture.CreateDbContext();
        Assert.Equal(fixture.Unrelated.Id, (await db.TaskMentions.SingleAsync(item => item.TaskId == taskId)).UserId);
        Assert.Single(await db.InboxItems
            .Where(item => item.TaskId == taskId && item.ActivityType == InboxActivityType.TaskMentioned)
            .ToListAsync());
    }

    [Fact]
    public async Task CreateTask_RejectsMentionOfUnknownUser()
    {
        var fixture = await TestFixture.CreateAsync();
        var unknownUserId = Guid.NewGuid();
        var mention = TaskMentionSyntax.CreateVisibleMention("Missing User");

        await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.CreateStore(fixture.Creator).CreateAsync(
                NewRequest(fixture.Assignee.Id) with
                {
                    Description = mention,
                    DescriptionMentionUserIds = [unknownUserId]
                }));

        await AssertNoTasksAsync(fixture);
    }

    [Fact]
    public async Task EditingMentions_AddsAndRemovesReferencesWithoutDuplicateNotifications()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));
        var before = await store.LoadDetailsAsync(taskId);
        var mention = TaskMentionSyntax.CreateVisibleMention(fixture.Unrelated.Name);

        var mentioned = await store.UpdateContentAsync(taskId, new(
            before.Title,
            $"Problem owner: {mention}",
            before.Version,
            before.Priority,
            before.ProjectId,
            DescriptionMentionUserIds: [fixture.Unrelated.Id]));
        var unchangedMention = await store.UpdateContentAsync(taskId, new(
            "Renamed task",
            mentioned.Description,
            mentioned.Version,
            mentioned.Priority,
            mentioned.ProjectId));
        await store.UpdateContentAsync(taskId, new(
            unchangedMention.Title,
            string.Empty,
            unchangedMention.Version,
            unchangedMention.Priority,
            unchangedMention.ProjectId));

        await using var db = fixture.CreateDbContext();
        var references = await db.TaskMentions.Where(item => item.TaskId == taskId).ToListAsync();
        var notifications = await db.InboxItems
            .Where(item => item.TaskId == taskId && item.ActivityType == InboxActivityType.TaskMentioned)
            .ToListAsync();
        Assert.Empty(references);
        Assert.Single(notifications);
        Assert.Equal(fixture.Unrelated.Id, notifications[0].RecipientUserId);
        Assert.Empty(fixture.Notifier.UpdatedNotifications);
    }

    [Fact]
    public async Task SelfMention_CreatesReferenceWithoutInboxNotification()
    {
        var fixture = await TestFixture.CreateAsync();
        var mention = TaskMentionSyntax.CreateVisibleMention(fixture.Creator.Name);

        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(
            NewRequest(fixture.Creator.Id) with
            {
                Description = mention,
                DescriptionMentionUserIds = [fixture.Creator.Id]
            });

        await using var db = fixture.CreateDbContext();
        Assert.Single(await db.TaskMentions.Where(item => item.TaskId == taskId).ToListAsync());
        Assert.DoesNotContain(await db.InboxItems.ToListAsync(),
            item => item.ActivityType == InboxActivityType.TaskMentioned);
    }

    [Fact]
    public async Task LegacyMentionToken_LoadsAndSavesAsVisibleNameOnly()
    {
        var fixture = await TestFixture.CreateAsync();
        var legacyToken = TaskMentionSyntax.CreateLegacyToken(fixture.Unrelated.Id, fixture.Unrelated.Name);
        var store = fixture.CreateStore(fixture.Creator);

        var taskId = await store.CreateAsync(
            NewRequest(fixture.Assignee.Id) with { Description = $"Review with {legacyToken}" });
        var details = await store.LoadDetailsAsync(taskId);

        Assert.Contains("@Unrelated User", details.Description);
        Assert.DoesNotContain("luma-user:", details.Description);
        Assert.DoesNotContain(fixture.Unrelated.Id.ToString(), details.Description);
        Assert.Equal(fixture.Unrelated.Id, Assert.Single(details.Mentions).UserId);
    }

    [Fact]
    public async Task CreateTask_SavesMultipleImageAttachmentsOutsideTaskRecord()
    {
        var fixture = await TestFixture.CreateAsync();
        var uploads = new[]
        {
            ImageUpload("dashboard.png", "image/png", PngBytes(1, 2, 3)),
            ImageUpload("failure.jpg", "image/jpeg", [0xFF, 0xD8, 0xFF, 0x01, 0x02])
        };

        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(
            NewRequest(fixture.Assignee.Id) with { Attachments = uploads });

        await using var db = fixture.CreateDbContext();
        var saved = await db.TaskAttachments.AsNoTracking()
            .Where(attachment => attachment.TaskId == taskId)
            .OrderBy(attachment => attachment.OriginalFileName)
            .ToListAsync();
        Assert.Equal(2, saved.Count);
        Assert.All(saved, attachment =>
        {
            Assert.Equal(fixture.Creator.Id, attachment.UploadedByUserId);
            Assert.NotEmpty(attachment.StorageKey);
            Assert.True(attachment.SizeBytes > 0);
        });
        Assert.Equal(2, fixture.AttachmentStorage.Files.Count);

        var details = await fixture.CreateStore(fixture.Assignee).LoadDetailsAsync(taskId);
        Assert.Equal(2, details.Attachments.Count);
        Assert.All(details.Attachments, attachment =>
            Assert.Equal($"/task-attachments/{attachment.Id:D}", attachment.Url));
    }

    [Fact]
    public async Task CreateTask_ResolvesInlineMarkdownImageToStoredAttachmentUrl()
    {
        var fixture = await TestFixture.CreateAsync();
        var token = Guid.NewGuid().ToString("N");
        var upload = ImageUpload("inline.png", "image/png", PngBytes(7, 8, 9)) with
        {
            InlineToken = token
        };

        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(
            NewRequest(fixture.Assignee.Id) with
            {
                Description = $"Before\n\n{TaskMarkdownImageSyntax.CreateMarkdown(upload.FileName, token)}\n\nAfter",
                Attachments = [upload]
            });

        var details = await fixture.CreateStore(fixture.Assignee).LoadDetailsAsync(taskId);
        var attachment = Assert.Single(details.Attachments);
        Assert.Contains($"![inline.png]({attachment.Url})", details.Description);
        Assert.DoesNotContain("luma-task-image:", details.Description);
        Assert.DoesNotContain("data:image", details.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateTask_RejectsBase64MarkdownImageData()
    {
        var fixture = await TestFixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.CreateStore(fixture.Creator).CreateAsync(
                NewRequest(fixture.Assignee.Id) with
                {
                    Description = "![Embedded](data:image/png;base64,AAAA)"
                }));

        Assert.Contains("Paste or upload", exception.Message);
        await AssertNoTasksAsync(fixture);
    }

    [Fact]
    public async Task CreateAndEditTask_PreserveExternalMarkdownImageWithoutFetchingIt()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        const string unreachableImage = "![Remote screenshot](https://127.0.0.1:1/unreachable.png)";

        var taskId = await store.CreateAsync(
            NewRequest(fixture.Assignee.Id) with { Description = unreachableImage });
        var created = await store.LoadDetailsAsync(taskId);

        Assert.Equal(unreachableImage, created.Description);
        Assert.Empty(created.Attachments);

        var editedDescription = $"Context\n\n{unreachableImage}";
        var edited = await store.UpdateContentAsync(taskId, new(
            created.Title,
            editedDescription,
            created.Version,
            created.Priority,
            created.ProjectId));

        Assert.Equal(editedDescription, edited.Description);
        Assert.Empty(edited.Attachments);
    }

    [Fact]
    public async Task MakerEdit_ResolvesNewInlineMarkdownImageAndPreservesItsPosition()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));
        var before = await store.LoadDetailsAsync(taskId);
        var token = Guid.NewGuid().ToString("N");
        var upload = ImageUpload("result.webp", "image/webp", WebpBytes()) with
        {
            InlineToken = token
        };
        var pendingMarkdown = TaskMarkdownImageSyntax.CreateMarkdown(upload.FileName, token);

        var updated = await store.UpdateContentAsync(taskId, new(
            before.Title,
            $"Top\n\n{pendingMarkdown}\n\nBottom",
            before.Version,
            before.Priority,
            before.ProjectId,
            NewAttachments: [upload]));

        var attachment = Assert.Single(updated.Attachments);
        Assert.Equal($"Top\n\n![result.webp]({attachment.Url})\n\nBottom", updated.Description);
    }

    [Fact]
    public async Task CreateTask_RejectsInvalidOrMismatchedImageContent()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);

        await Assert.ThrowsAsync<ValidationException>(() => store.CreateAsync(
            NewRequest(fixture.Assignee.Id) with
            {
                Attachments = [ImageUpload("not-an-image.png", "image/png", "plain text"u8.ToArray())]
            }));
        await Assert.ThrowsAsync<ValidationException>(() => store.CreateAsync(
            NewRequest(fixture.Assignee.Id) with
            {
                Attachments = [ImageUpload("wrong.jpg", "image/jpeg", PngBytes())]
            }));

        await AssertNoTasksAsync(fixture);
        Assert.Empty(fixture.AttachmentStorage.Files);
    }

    [Fact]
    public async Task CreateTask_RejectsImageOverServerSizeLimit()
    {
        var fixture = await TestFixture.CreateAsync();
        var oversized = ImageUpload(
            "large.png",
            "image/png",
            PngBytes(),
            TaskAttachmentRules.MaximumFileSizeBytes + 1);

        await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.CreateStore(fixture.Creator).CreateAsync(
                NewRequest(fixture.Assignee.Id) with { Attachments = [oversized] }));

        await AssertNoTasksAsync(fixture);
        Assert.Empty(fixture.AttachmentStorage.Files);
    }

    [Fact]
    public async Task Maker_CanAddAndRemoveTaskAttachments()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id) with
        {
            Attachments = [ImageUpload("before.png", "image/png", PngBytes(1))]
        });
        var before = await store.LoadDetailsAsync(taskId);

        var updated = await store.UpdateContentAsync(taskId, new(
            before.Title,
            before.Description,
            before.Version,
            before.Priority,
            before.ProjectId,
            NewAttachments: [ImageUpload("after.gif", "image/gif", "GIF89a-data"u8.ToArray())],
            RemovedAttachmentIds: [before.Attachments.Single().Id]));

        Assert.Single(updated.Attachments);
        Assert.Equal("after.gif", updated.Attachments.Single().FileName);
        Assert.Single(fixture.AttachmentStorage.Files);
        await using var db = fixture.CreateDbContext();
        Assert.Single(await db.TaskAttachments.Where(item => item.TaskId == taskId).ToListAsync());
    }

    [Fact]
    public async Task NonMaker_CannotRemoveTaskAttachment()
    {
        var fixture = await TestFixture.CreateAsync();
        var makerStore = fixture.CreateStore(fixture.Creator);
        var taskId = await makerStore.CreateAsync(NewRequest(fixture.Assignee.Id) with
        {
            Attachments = [ImageUpload("evidence.png", "image/png", PngBytes())]
        });
        var details = await fixture.CreateStore(fixture.Assignee).LoadDetailsAsync(taskId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.CreateStore(fixture.Assignee).UpdateContentAsync(taskId, new(
                details.Title,
                details.Description,
                details.Version,
                details.Priority,
                details.ProjectId,
                RemovedAttachmentIds: [details.Attachments.Single().Id])));

        Assert.Single(fixture.AttachmentStorage.Files);
        await using var db = fixture.CreateDbContext();
        Assert.Single(await db.TaskAttachments.Where(item => item.TaskId == taskId).ToListAsync());
    }

    [Fact]
    public async Task CreateTask_RejectsOversizedDescription()
    {
        var fixture = await TestFixture.CreateAsync();
        var request = NewRequest(fixture.Assignee.Id) with
        {
            Description = new string('d', 10001)
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.CreateStore(fixture.Creator).CreateAsync(request));
        await AssertNoTasksAsync(fixture);
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
    public async Task ExistingRegisteredEmail_UsesNormalAssignmentWithoutInvitation()
    {
        var fixture = await TestFixture.CreateAsync();
        var request = NewEmailRequest(fixture.Assignee.Email.ToUpperInvariant());

        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(request);

        await using var db = fixture.CreateDbContext();
        var task = await db.Tasks.SingleAsync();
        Assert.Equal(taskId, task.Id);
        Assert.Equal(fixture.Assignee.Id, task.AssigneeId);
        Assert.Empty(await db.TaskInvitations.ToListAsync());
    }

    [Fact]
    public async Task UnknownEmail_CreatesPendingInvitationWithoutFakeUser()
    {
        var fixture = await TestFixture.CreateAsync();
        int userCount;
        await using (var before = fixture.CreateDbContext())
            userCount = await before.Users.CountAsync();

        var taskId = await fixture.CreateStore(fixture.Creator)
            .CreateAsync(NewEmailRequest("  Anna.New@Example.com  "));

        await using var db = fixture.CreateDbContext();
        var task = await db.Tasks.SingleAsync();
        var invitation = await db.TaskInvitations.SingleAsync();
        Assert.Equal(taskId, task.Id);
        Assert.Null(task.AssigneeId);
        Assert.Equal(taskId, invitation.TaskId);
        Assert.Equal(fixture.Creator.Id, invitation.InviterId);
        Assert.Equal("Anna.New@Example.com", invitation.RecipientEmail);
        Assert.Equal("ANNA.NEW@EXAMPLE.COM", invitation.NormalizedRecipientEmail);
        Assert.Equal(TaskInvitationStatus.Pending, invitation.Status);
        Assert.True(invitation.ExpiresUtc > invitation.CreatedUtc);
        Assert.Equal(userCount, await db.Users.CountAsync());
    }

    [Fact]
    public async Task MakerSeesInvitedTaskAndInvitationEmailTargetsOnlyRecipient()
    {
        var fixture = await TestFixture.CreateAsync();
        const string invitedEmail = "new.doer@example.com";
        var store = fixture.CreateStore(fixture.Creator);

        var taskId = await store.CreateAsync(NewEmailRequest(invitedEmail) with { Priority = TaskPriority.Urgent });

        var summary = Assert.Single(await store.LoadCreatedAsync());
        Assert.Equal(taskId, summary.Id);
        Assert.Equal(invitedEmail, summary.AssigneeName);
        Assert.True(summary.IsInvited);
        var details = await store.LoadDetailsAsync(taskId);
        Assert.True(details.IsInvited);
        Assert.Equal(invitedEmail, details.AssigneeName);

        var notification = Assert.Single(fixture.Notifier.CreatedNotifications);
        var recipient = Assert.Single(notification.Recipients);
        Assert.Equal(TaskNotificationRole.Doer, recipient.Role);
        Assert.Equal(invitedEmail, recipient.Email);
        Assert.Equal(TaskPriority.Urgent, notification.Priority);
        Assert.Contains("/task-invitation?token=", notification.TaskUrl);
    }

    [Fact]
    public async Task SelfAssignmentByEmail_DoesNotCreateInvitationOrEmail()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);

        await store.CreateAsync(NewEmailRequest(fixture.Creator.Email.ToUpperInvariant()));

        await using var db = fixture.CreateDbContext();
        Assert.Equal(fixture.Creator.Id, (await db.Tasks.SingleAsync()).AssigneeId);
        Assert.Empty(await db.TaskInvitations.ToListAsync());
        Assert.Empty(fixture.Notifier.CreatedNotifications);
    }

    [Fact]
    public async Task InvitationEmailFailure_DoesNotUndoTaskOrInvitation()
    {
        var fixture = await TestFixture.CreateAsync();
        fixture.Notifier.FailCreated = true;
        var store = fixture.CreateStore(fixture.Creator);

        var taskId = await store.CreateAsync(NewEmailRequest("delivery.failure@example.com"));

        await using var db = fixture.CreateDbContext();
        Assert.Equal(taskId, (await db.Tasks.SingleAsync()).Id);
        Assert.Equal(TaskInvitationStatus.Pending, (await db.TaskInvitations.SingleAsync()).Status);
        Assert.Contains("invitation email could not be sent", store.LastNotice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MatchingNewUserClaimsInvitationAndReceivesExistingPendingTask()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator)
            .CreateAsync(NewEmailRequest("future.user@example.com"));
        var token = InvitationToken(fixture.Notifier.CreatedNotifications.Single().TaskUrl);
        var service = new TaskInvitationService(new TestDbContextFactory(fixture.Options));
        var beforeRegistration = await service.InspectAsync(token);
        Assert.Equal(TaskInvitationAccessStatus.Valid, beforeRegistration.Status);
        Assert.False(beforeRegistration.AccountExists);
        var newUser = TestFixture.NewUser("future.user@example.com", "Future User");
        await using (var db = fixture.CreateDbContext())
        {
            db.Users.Add(newUser);
            await db.SaveChangesAsync();
        }

        var afterRegistration = await service.InspectAsync(token);
        Assert.True(afterRegistration.AccountExists);
        var result = await service.ClaimAsync(token, newUser.Id);

        Assert.Equal(TaskInvitationClaimStatus.Success, result.Status);
        Assert.Equal(taskId, result.TaskId);
        var assigned = Assert.Single(await fixture.CreateStore(newUser).LoadAssignedAsync());
        Assert.Equal(taskId, assigned.Id);
        Assert.Equal(TaskAssignmentStatus.Pending, assigned.AssignmentStatus);
        await using var verification = fixture.CreateDbContext();
        Assert.Single(await verification.Tasks.ToListAsync());
        Assert.Equal(newUser.Id, (await verification.Tasks.SingleAsync()).AssigneeId);
        Assert.Equal(TaskInvitationStatus.Claimed, (await verification.TaskInvitations.SingleAsync()).Status);
    }

    [Fact]
    public async Task DifferentUserCannotClaimAndUsedInvitationCannotCreateDuplicateTask()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator)
            .CreateAsync(NewEmailRequest("claim.once@example.com"));
        var token = InvitationToken(fixture.Notifier.CreatedNotifications.Single().TaskUrl);
        var invitedUser = TestFixture.NewUser("claim.once@example.com", "Claim Once");
        await using (var db = fixture.CreateDbContext())
        {
            db.Users.Add(invitedUser);
            await db.SaveChangesAsync();
        }

        var service = new TaskInvitationService(new TestDbContextFactory(fixture.Options));
        Assert.Equal(TaskInvitationClaimStatus.EmailMismatch,
            (await service.ClaimAsync(token, fixture.Unrelated.Id)).Status);
        Assert.Equal(TaskInvitationClaimStatus.Success,
            (await service.ClaimAsync(token, invitedUser.Id)).Status);
        Assert.Equal(TaskInvitationClaimStatus.Invalid,
            (await service.ClaimAsync(token, invitedUser.Id)).Status);
        Assert.Equal(TaskInvitationAccessStatus.Invalid, (await service.InspectAsync(token)).Status);

        await using var verification = fixture.CreateDbContext();
        Assert.Single(await verification.Tasks.ToListAsync());
        Assert.Single(await verification.TaskInvitations.ToListAsync());
        Assert.Equal(taskId, (await verification.TaskInvitations.SingleAsync()).TaskId);
    }

    [Fact]
    public async Task InvalidAndExpiredInvitationTokensAreRejected()
    {
        var fixture = await TestFixture.CreateAsync();
        await fixture.CreateStore(fixture.Creator).CreateAsync(NewEmailRequest("expired@example.com"));
        var token = InvitationToken(fixture.Notifier.CreatedNotifications.Single().TaskUrl);
        var service = new TaskInvitationService(new TestDbContextFactory(fixture.Options));

        Assert.Equal(TaskInvitationAccessStatus.Invalid, (await service.InspectAsync("not-a-token")).Status);
        await using (var db = fixture.CreateDbContext())
        {
            var invitation = await db.TaskInvitations.SingleAsync();
            invitation.ExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        Assert.Equal(TaskInvitationAccessStatus.Expired, (await service.InspectAsync(token)).Status);
        Assert.Equal(TaskInvitationClaimStatus.Expired,
            (await service.ClaimAsync(token, fixture.Assignee.Id)).Status);
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
    public async Task EmptyAssigneeId_IsRejected()
    {
        var fixture = await TestFixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(Guid.Empty)));

        Assert.Contains("valid task assignee", exception.Message);
        await AssertNoTasksAsync(fixture);
    }

    [Fact]
    public async Task Task_CanBeCreatedUnassignedWithoutCreatingInvitationOrNotification()
    {
        var fixture = await TestFixture.CreateAsync();
        var request = NewRequest(fixture.Assignee.Id) with
        {
            AssigneeId = null,
            AssigneeEmail = null
        };

        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(request);

        await using var db = fixture.CreateDbContext();
        var task = await db.Tasks.SingleAsync();
        Assert.Equal(taskId, task.Id);
        Assert.Null(task.AssigneeId);
        Assert.Equal(TaskAssignmentStatus.Pending, task.AssignmentStatus);
        Assert.Empty(await db.TaskInvitations.ToListAsync());
        Assert.Empty(fixture.Notifier.CreatedNotifications);
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
    public async Task Task_CanBeCreatedWithoutDeadline()
    {
        var fixture = await TestFixture.CreateAsync();
        var request = NewRequest(fixture.Assignee.Id) with { Deadline = null };

        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(request);

        await using var db = fixture.CreateDbContext();
        Assert.Null((await db.Tasks.SingleAsync(task => task.Id == taskId)).Deadline);
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
            fixture.AttachmentStorage,
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
    public async Task TaskListSummaries_ExposeBoardDragPermissionOnlyToDoer()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        var assigned = Assert.Single(await fixture.CreateStore(fixture.Assignee).LoadAssignedAsync());
        var created = Assert.Single(await fixture.CreateStore(fixture.Creator).LoadCreatedAsync());

        Assert.Equal(taskId, assigned.Id);
        Assert.NotEqual(Guid.Empty, assigned.Version);
        Assert.True(assigned.CanManageWorkStatus);
        Assert.Equal(taskId, created.Id);
        Assert.Equal(assigned.Version, created.Version);
        Assert.False(created.CanManageWorkStatus);
    }

    [Fact]
    public async Task SelfAssignedTask_IsBoardDraggableInBothOwnershipViews()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Creator.Id));

        var assigned = Assert.Single(await store.LoadAssignedAsync());
        var created = Assert.Single(await store.LoadCreatedAsync());

        Assert.Equal(taskId, assigned.Id);
        Assert.Equal(taskId, created.Id);
        Assert.True(assigned.CanManageWorkStatus);
        Assert.True(created.CanManageWorkStatus);
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
    public async Task UnrelatedAuthenticatedUser_CanOpenTaskDetailsReadOnly()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        var details = await fixture.CreateStore(fixture.Unrelated).LoadDetailsAsync(taskId);

        Assert.Equal(taskId, details.Id);
        Assert.False(details.CanAccept);
        Assert.False(details.CanReviewDeadlineChange);
        Assert.False(details.CanEdit);
        Assert.False(details.CanManageWorkStatus);
        Assert.False(details.CanComment);
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
    public async Task AuthenticatedUser_CanTakeUnassignedTask_AndItIsAcceptedImmediately()
    {
        var fixture = await TestFixture.CreateAsync();
        var request = NewRequest(fixture.Assignee.Id) with
        {
            AssigneeId = null,
            AssigneeEmail = null,
            Deadline = null
        };
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(request);
        var takerStore = fixture.CreateStore(fixture.Unrelated);
        var before = await takerStore.LoadDetailsAsync(taskId);

        var taken = await takerStore.TakeAsync(taskId, new TakeLumaTaskRequest(before.Version));

        Assert.Equal(fixture.Unrelated.Name, taken.AssigneeName);
        Assert.Equal(TaskAssignmentStatus.Accepted, taken.AssignmentStatus);
        Assert.Equal(TaskWorkStatus.ToDo, taken.WorkStatus);
        Assert.NotNull(taken.AcceptedAt);
        Assert.True(taken.CanManageWorkStatus);
        Assert.False(taken.CanTake);
        var inProgress = await takerStore.ChangeWorkStatusAsync(
            taskId, new ChangeTaskWorkStatusRequest(TaskWorkStatus.InProgress, taken.Version));
        Assert.Equal(TaskWorkStatus.InProgress, inProgress.WorkStatus);
        await using var db = fixture.CreateDbContext();
        var persisted = await db.Tasks.SingleAsync(task => task.Id == taskId);
        Assert.Equal(fixture.Unrelated.Id, persisted.AssigneeId);
        Assert.Equal(TaskAssignmentStatus.Accepted, persisted.AssignmentStatus);
        Assert.Equal(TaskWorkStatus.InProgress, persisted.WorkStatus);
        Assert.NotNull(persisted.AcceptedAt);
    }

    [Fact]
    public async Task AssignedOrInvitedTask_CannotBeTaken()
    {
        var fixture = await TestFixture.CreateAsync();
        var assignedId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        var invitedId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewEmailRequest("future.doer@example.com"));
        var store = fixture.CreateStore(fixture.Unrelated);
        var assigned = await store.LoadDetailsAsync(assignedId);
        var invited = await store.LoadDetailsAsync(invitedId);

        await Assert.ThrowsAsync<ValidationException>(() =>
            store.TakeAsync(assignedId, new TakeLumaTaskRequest(assigned.Version)));
        await Assert.ThrowsAsync<ValidationException>(() =>
            store.TakeAsync(invitedId, new TakeLumaTaskRequest(invited.Version)));
    }

    [Fact]
    public async Task TakeTask_RejectsStaleVersion()
    {
        var fixture = await TestFixture.CreateAsync();
        var request = NewRequest(fixture.Assignee.Id) with { AssigneeId = null, AssigneeEmail = null };
        var makerStore = fixture.CreateStore(fixture.Creator);
        var taskId = await makerStore.CreateAsync(request);
        var stale = await fixture.CreateStore(fixture.Unrelated).LoadDetailsAsync(taskId);
        await makerStore.UpdateContentAsync(taskId, new(
            "Updated title", request.Description, stale.Version, TaskPriority.Low));

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.CreateStore(fixture.Unrelated).TakeAsync(taskId, new TakeLumaTaskRequest(stale.Version)));

        Assert.Contains("changed in another session", exception.Message);
        await using var db = fixture.CreateDbContext();
        Assert.Null((await db.Tasks.SingleAsync(task => task.Id == taskId)).AssigneeId);
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
        Assert.Empty(fixture.Notifier.AcceptedNotifications);
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
    public async Task TaskAcceptance_DoesNotSendEmail()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        await fixture.CreateStore(fixture.Assignee).AcceptAsync(taskId);

        Assert.Empty(fixture.Notifier.AcceptedNotifications);
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
    public async Task TaskAcceptance_DoesNotInvokeEmailNotifier()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        fixture.Notifier.FailAccepted = true;
        var store = fixture.CreateStore(fixture.Assignee);

        var accepted = await store.AcceptAsync(taskId);

        await using var db = fixture.CreateDbContext();
        Assert.Equal(TaskAssignmentStatus.Accepted, (await db.Tasks.SingleAsync()).AssignmentStatus);
        Assert.Equal(TaskAssignmentStatus.Accepted, accepted.AssignmentStatus);
        Assert.Empty(fixture.Notifier.AcceptedNotifications);
        Assert.Null(store.LastNotice);
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
    public async Task Maker_CanEditTitleAndStructuredDescription()
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
    public async Task Maker_CanEditMarkdownDescription()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));
        var before = await store.LoadDetailsAsync(taskId);

        var updated = await store.UpdateContentAsync(taskId, new(
            before.Title,
            "## Problem\nThe export is incomplete.\n\n## Expected Result\n\n- Every row is exported",
            before.Version));

        Assert.Equal("## Problem\nThe export is incomplete.\n\n## Expected Result\n\n- Every row is exported", updated.Description);
        await using var db = fixture.CreateDbContext();
        var saved = await db.Tasks.SingleAsync(item => item.Id == taskId);
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
        Assert.Empty(fixture.Notifier.UpdatedNotifications);
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

        var updated = await store.UpdateContentAsync(taskId, new(
            "Revised title", requested.Description, requested.Version, TaskPriority.High));

        Assert.Equal(TaskAssignmentStatus.DeadlineChangeRequested, updated.AssignmentStatus);
        Assert.Equal(requested.RequestedDeadline, updated.RequestedDeadline);
        Assert.Equal(requested.DeadlineChangeComment, updated.DeadlineChangeComment);
        Assert.Equal(requested.DeadlineChangeRequestedAt, updated.DeadlineChangeRequestedAt);
        Assert.Equal(requested.Deadline, updated.Deadline);
        Assert.Equal(TaskPriority.High, updated.Priority);
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
    public async Task MakerEdit_DoesNotSendEmail()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));
        var before = await store.LoadDetailsAsync(taskId);

        await store.UpdateContentAsync(taskId, new("Revised title", before.Description, before.Version));

        Assert.Empty(fixture.Notifier.UpdatedNotifications);
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
    public async Task DoerStatusChange_DoesNotSendEmail()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        var store = fixture.CreateStore(fixture.Assignee);
        var accepted = await store.AcceptAsync(taskId);

        await store.ChangeWorkStatusAsync(taskId, new(TaskWorkStatus.InProgress, accepted.Version));

        Assert.Empty(fixture.Notifier.WorkStatusNotifications);
    }

    [Fact]
    public async Task EditAndStatusChange_DoNotInvokeEmailNotifier()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        fixture.Notifier.FailUpdated = true;
        var makerStore = fixture.CreateStore(fixture.Creator);
        var before = await makerStore.LoadDetailsAsync(taskId);
        var edited = await makerStore.UpdateContentAsync(taskId, new("Persisted edit", before.Description, before.Version));
        Assert.Equal("Persisted edit", edited.Title);
        Assert.Empty(fixture.Notifier.UpdatedNotifications);
        Assert.Null(makerStore.LastNotice);

        var doerStore = fixture.CreateStore(fixture.Assignee);
        var accepted = await doerStore.AcceptAsync(taskId);
        fixture.Notifier.FailWorkStatus = true;
        var started = await doerStore.ChangeWorkStatusAsync(taskId, new(TaskWorkStatus.InProgress, accepted.Version));
        Assert.Equal(TaskWorkStatus.InProgress, started.WorkStatus);
        Assert.Empty(fixture.Notifier.WorkStatusNotifications);
        Assert.Null(doerStore.LastNotice);

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
    public async Task CommentMention_PersistsUserRelationshipAndCreatesInboxNotification()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));
        var visibleMention = TaskMentionSyntax.CreateVisibleMention(fixture.Unrelated.Name);

        var comment = await store.AddCommentAsync(
            taskId,
            new($"Please review this, {visibleMention}.", [fixture.Unrelated.Id]));

        Assert.Contains("@Unrelated User", comment.Text);
        Assert.DoesNotContain("luma-user:", comment.Text);
        Assert.DoesNotContain(fixture.Unrelated.Id.ToString(), comment.Text);
        await using var db = fixture.CreateDbContext();
        var mention = Assert.Single(await db.TaskCommentMentions.ToListAsync());
        Assert.Equal(comment.Id, mention.CommentId);
        Assert.Equal(fixture.Unrelated.Id, mention.UserId);
        var inboxItem = Assert.Single(await db.InboxItems
            .Where(item => item.ActivityType == InboxActivityType.TaskMentioned)
            .ToListAsync());
        AssertInbox(inboxItem, InboxActivityType.TaskMentioned, fixture.Creator, fixture.Unrelated, taskId);
        Assert.Empty(fixture.Notifier.CommentNotifications);
    }

    [Fact]
    public async Task Comment_ResolvesUniqueVisibleMentionFromPastedText()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        await fixture.CreateStore(fixture.Assignee).AddCommentAsync(
            taskId,
            new("Pasted mention for @Unrelated User"));

        await using var db = fixture.CreateDbContext();
        Assert.Equal(fixture.Unrelated.Id, (await db.TaskCommentMentions.SingleAsync()).UserId);
        var notification = Assert.Single(await db.InboxItems
            .Where(item => item.ActivityType == InboxActivityType.TaskMentioned)
            .ToListAsync());
        Assert.Equal(fixture.Assignee.Id, notification.ActorUserId);
        Assert.Equal(fixture.Unrelated.Id, notification.RecipientUserId);
        Assert.Equal(taskId, notification.TaskId);
    }

    [Fact]
    public async Task Comment_KeepsAmbiguousOrUnknownPastedMentionsAsPlainText()
    {
        var fixture = await TestFixture.CreateAsync();
        await using (var db = fixture.CreateDbContext())
        {
            db.Users.AddRange(
                TestFixture.NewUser("alex.one@luma.test", "Alex Smith"),
                TestFixture.NewUser("alex.two@luma.test", "Alex Smith"));
            await db.SaveChangesAsync();
        }
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));

        var comment = await store.AddCommentAsync(
            taskId,
            new("Ask @Alex Smith and @Person Who Does Not Exist"));

        Assert.Contains("@Alex Smith", comment.Text);
        Assert.Contains("@Person Who Does Not Exist", comment.Text);
        await using var verify = fixture.CreateDbContext();
        Assert.Empty(await verify.TaskCommentMentions.ToListAsync());
        Assert.DoesNotContain(await verify.InboxItems.ToListAsync(),
            item => item.ActivityType == InboxActivityType.TaskMentioned);
    }

    [Fact]
    public async Task CommentMentions_DoNotNotifyActorOrDuplicateOtherPartyNotification()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));

        await store.AddCommentAsync(
            taskId,
            new(
                $"{TaskMentionSyntax.CreateVisibleMention(fixture.Creator.Name)} " +
                $"{TaskMentionSyntax.CreateVisibleMention(fixture.Assignee.Name)}",
                [fixture.Creator.Id, fixture.Assignee.Id, fixture.Assignee.Id]));

        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, await db.TaskCommentMentions.CountAsync());
        var commentItems = await db.InboxItems
            .Where(item => item.ActivityType == InboxActivityType.CommentAdded ||
                           item.ActivityType == InboxActivityType.TaskMentioned)
            .ToListAsync();
        var notification = Assert.Single(commentItems);
        Assert.Equal(InboxActivityType.TaskMentioned, notification.ActivityType);
        Assert.Equal(fixture.Assignee.Id, notification.RecipientUserId);
        Assert.DoesNotContain(commentItems, item => item.RecipientUserId == fixture.Creator.Id);
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
    public async Task MakerComment_DoesNotSendEmail()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));

        await store.AddCommentAsync(taskId, new("Maker comment"));

        Assert.Empty(fixture.Notifier.CommentNotifications);
    }

    [Fact]
    public async Task DoerComment_DoesNotSendEmail()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        await fixture.CreateStore(fixture.Assignee).AddCommentAsync(taskId, new("Doer comment"));

        Assert.Empty(fixture.Notifier.CommentNotifications);
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
    public async Task Comment_DoesNotInvokeEmailNotifier()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));
        fixture.Notifier.FailComment = true;

        var comment = await store.AddCommentAsync(taskId, new("Saved without email."));

        Assert.Empty(fixture.Notifier.CommentNotifications);
        Assert.Null(store.LastNotice);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(comment.Id, (await db.TaskComments.SingleAsync()).Id);
    }

    [Fact]
    public async Task NewTask_DefaultsToNoPriority()
    {
        var fixture = await TestFixture.CreateAsync();

        await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        await using var db = fixture.CreateDbContext();
        Assert.Equal(TaskPriority.None, (await db.Tasks.SingleAsync()).Priority);
    }

    [Fact]
    public async Task Maker_CanCreateTaskWithPriority()
    {
        var fixture = await TestFixture.CreateAsync();

        await fixture.CreateStore(fixture.Creator).CreateAsync(
            NewRequest(fixture.Assignee.Id) with { Priority = TaskPriority.High });

        await using var db = fixture.CreateDbContext();
        Assert.Equal(TaskPriority.High, (await db.Tasks.SingleAsync()).Priority);
    }

    [Fact]
    public async Task Maker_CanChangePriorityAndPreserveAssignmentAndWorkStatus()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        var doerStore = fixture.CreateStore(fixture.Assignee);
        var accepted = await doerStore.AcceptAsync(taskId);
        await doerStore.ChangeWorkStatusAsync(taskId, new(TaskWorkStatus.InProgress, accepted.Version));
        var makerStore = fixture.CreateStore(fixture.Creator);
        var before = await makerStore.LoadDetailsAsync(taskId);

        var updated = await makerStore.UpdateContentAsync(taskId, new(
            before.Title, before.Description, before.Version, TaskPriority.Urgent));

        Assert.Equal(TaskPriority.Urgent, updated.Priority);
        Assert.Equal(TaskAssignmentStatus.Accepted, updated.AssignmentStatus);
        Assert.Equal(TaskWorkStatus.InProgress, updated.WorkStatus);
        Assert.Equal(before.AcceptedAt, updated.AcceptedAt);
        Assert.Equal(before.Deadline, updated.Deadline);
    }

    [Fact]
    public async Task DoerAndUnrelatedUser_CannotChangePriority()
    {
        var fixture = await TestFixture.CreateAsync();
        var makerStore = fixture.CreateStore(fixture.Creator);
        var taskId = await makerStore.CreateAsync(NewRequest(fixture.Assignee.Id));
        var details = await makerStore.LoadDetailsAsync(taskId);
        var request = new UpdateLumaTaskContentRequest(
            details.Title, details.Description, details.Version, TaskPriority.High);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.CreateStore(fixture.Assignee).UpdateContentAsync(taskId, request));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.CreateStore(fixture.Unrelated).UpdateContentAsync(taskId, request));

        await using var db = fixture.CreateDbContext();
        Assert.Equal(TaskPriority.None, (await db.Tasks.SingleAsync()).Priority);
    }

    [Fact]
    public async Task PriorityChange_DoesNotSendEmail()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id));
        var before = await store.LoadDetailsAsync(taskId);

        await store.UpdateContentAsync(taskId, new(
            before.Title, before.Description, before.Version, TaskPriority.High));

        Assert.Empty(fixture.Notifier.UpdatedNotifications);
    }

    [Fact]
    public async Task SelfAssignedPriorityChange_SendsNoEmail()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Creator.Id));
        var before = await store.LoadDetailsAsync(taskId);

        await store.UpdateContentAsync(taskId, new(
            before.Title, before.Description, before.Version, TaskPriority.Medium));

        Assert.Empty(fixture.Notifier.UpdatedNotifications);
    }

    [Fact]
    public async Task WorkStatusFilter_ReturnsMatchingAuthorizedTasks()
    {
        var fixture = await TestFixture.CreateAsync();
        await SeedTaskAsync(fixture, "To do", workStatus: TaskWorkStatus.ToDo);
        var expected = await SeedTaskAsync(fixture, "In progress", workStatus: TaskWorkStatus.InProgress);

        var tasks = await fixture.CreateStore(fixture.Creator).LoadCreatedAsync(
            new TaskListQuery(WorkStatus: TaskWorkStatus.InProgress));

        Assert.Equal(expected.Id, Assert.Single(tasks).Id);
    }

    [Fact]
    public async Task AssignmentStatusFilter_ReturnsMatchingTasks()
    {
        var fixture = await TestFixture.CreateAsync();
        await SeedTaskAsync(fixture, "Pending", assignmentStatus: TaskAssignmentStatus.Pending);
        var expected = await SeedTaskAsync(fixture, "Accepted", assignmentStatus: TaskAssignmentStatus.Accepted);

        var tasks = await fixture.CreateStore(fixture.Assignee).LoadAssignedAsync(
            new TaskListQuery(AssignmentStatus: TaskAssignmentStatus.Accepted));

        Assert.Equal(expected.Id, Assert.Single(tasks).Id);
    }

    [Fact]
    public async Task PriorityFilter_IncludingNoPriority_ReturnsMatchingTasks()
    {
        var fixture = await TestFixture.CreateAsync();
        var none = await SeedTaskAsync(fixture, "No priority", priority: TaskPriority.None);
        var urgent = await SeedTaskAsync(fixture, "Urgent", priority: TaskPriority.Urgent);
        var store = fixture.CreateStore(fixture.Creator);

        var noPriority = await store.LoadCreatedAsync(new TaskListQuery(Priority: TaskPriority.None));
        var urgentOnly = await store.LoadCreatedAsync(new TaskListQuery(Priority: TaskPriority.Urgent));

        Assert.Equal(none.Id, Assert.Single(noPriority).Id);
        Assert.Equal(urgent.Id, Assert.Single(urgentOnly).Id);
    }

    [Fact]
    public async Task AssigneeFilter_SupportsUnassignedAndSpecificUsers()
    {
        var fixture = await TestFixture.CreateAsync();
        var unassigned = await SeedTaskAsync(fixture, "Unassigned", unassigned: true);
        var assigned = await SeedTaskAsync(fixture, "Assigned", assignee: fixture.Assignee);
        await fixture.CreateStore(fixture.Creator).CreateAsync(NewEmailRequest("invited@example.com"));
        var store = fixture.CreateStore(fixture.Creator);

        var unassignedTasks = await store.LoadRelatedAsync(new TaskListQuery(IncludeUnassigned: true));
        var assignedTasks = await store.LoadRelatedAsync(new TaskListQuery(AssigneeIds: [fixture.Assignee.Id]));

        Assert.Equal(unassigned.Id, Assert.Single(unassignedTasks).Id);
        Assert.Equal(assigned.Id, Assert.Single(assignedTasks).Id);
    }

    [Fact]
    public async Task NoDeadlineFilter_ReturnsOnlyTasksWithoutDeadline()
    {
        var fixture = await TestFixture.CreateAsync();
        var noDeadline = await SeedTaskAsync(fixture, "No deadline", noDeadline: true);
        await SeedTaskAsync(fixture, "Has deadline");

        var tasks = await fixture.CreateStore(fixture.Creator).LoadRelatedAsync(
            new TaskListQuery(Deadline: TaskDeadlineFilter.NoDeadline));

        Assert.Equal(noDeadline.Id, Assert.Single(tasks).Id);
        Assert.Null(tasks[0].Deadline);
    }

    [Fact]
    public async Task AssigneeFilter_CombinesWithNullableDeadlineFilter()
    {
        var fixture = await TestFixture.CreateAsync();
        var expected = await SeedTaskAsync(
            fixture, "Assigned to me without deadline", creator: fixture.Assignee,
            assignee: fixture.Creator, noDeadline: true);
        await SeedTaskAsync(
            fixture, "Assigned to somebody else", creator: fixture.Assignee,
            assignee: fixture.Unrelated, noDeadline: true);
        await SeedTaskAsync(
            fixture, "Assigned to me with deadline", creator: fixture.Assignee,
            assignee: fixture.Creator);

        var tasks = await fixture.CreateStore(fixture.Creator).LoadRelatedAsync(new TaskListQuery(
            Deadline: TaskDeadlineFilter.NoDeadline,
            AssigneeIds: [fixture.Creator.Id]));

        Assert.Equal(expected.Id, Assert.Single(tasks).Id);
    }

    [Fact]
    public async Task DeadlineNearestSort_PlacesNoDeadlineAfterDatedTasks()
    {
        var fixture = await TestFixture.CreateAsync();
        var noDeadline = await SeedTaskAsync(fixture, "No deadline", noDeadline: true);
        var dated = await SeedTaskAsync(
            fixture, "Dated", deadline: DateOnly.FromDateTime(DateTime.Today.AddDays(20)));

        var tasks = await fixture.CreateStore(fixture.Creator).LoadRelatedAsync(
            new TaskListQuery(Sort: TaskSortOrder.DeadlineNearest));

        Assert.Equal([dated.Id, noDeadline.Id], tasks.Select(task => task.Id));
    }

    [Fact]
    public async Task OverdueAndTodayFilters_ReturnMatchingDeadlines()
    {
        var fixture = await TestFixture.CreateAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var overdue = await SeedTaskAsync(fixture, "Overdue", deadline: today.AddDays(-1));
        var dueToday = await SeedTaskAsync(fixture, "Due today", deadline: today);
        await SeedTaskAsync(fixture, "Future", deadline: today.AddDays(1));
        var store = fixture.CreateStore(fixture.Creator);

        var overdueTasks = await store.LoadCreatedAsync(new TaskListQuery(Deadline: TaskDeadlineFilter.Overdue));
        var todayTasks = await store.LoadCreatedAsync(new TaskListQuery(Deadline: TaskDeadlineFilter.Today));

        Assert.Equal(overdue.Id, Assert.Single(overdueTasks).Id);
        Assert.Equal(dueToday.Id, Assert.Single(todayTasks).Id);
    }

    [Fact]
    public async Task ThisWeekFilter_IncludesTodayThroughSunday()
    {
        var fixture = await TestFixture.CreateAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)today.DayOfWeek + 7) % 7;
        var endOfWeek = today.AddDays(daysUntilSunday);
        var todayTask = await SeedTaskAsync(fixture, "Today", deadline: today);
        var sundayTask = endOfWeek == today
            ? null
            : await SeedTaskAsync(fixture, "Sunday", deadline: endOfWeek);
        await SeedTaskAsync(fixture, "Next week", deadline: endOfWeek.AddDays(1));

        var tasks = await fixture.CreateStore(fixture.Creator).LoadCreatedAsync(
            new TaskListQuery(Deadline: TaskDeadlineFilter.ThisWeek));

        Assert.Contains(tasks, task => task.Id == todayTask.Id);
        if (sundayTask is not null) Assert.Contains(tasks, task => task.Id == sundayTask.Id);
        Assert.DoesNotContain(tasks, task => task.Title == "Next week");
    }

    [Fact]
    public async Task TitleSearch_IsCaseInsensitive()
    {
        var fixture = await TestFixture.CreateAsync();
        var expected = await SeedTaskAsync(fixture, "Payment verification");
        await SeedTaskAsync(fixture, "Launch notes");

        var tasks = await fixture.CreateStore(fixture.Creator).LoadCreatedAsync(
            new TaskListQuery(Search: "PAYMENT"));

        Assert.Equal(expected.Id, Assert.Single(tasks).Id);
    }

    [Fact]
    public async Task CombinedFilters_ReturnOnlyTasksMatchingEveryCondition()
    {
        var fixture = await TestFixture.CreateAsync();
        var expected = await SeedTaskAsync(
            fixture, "Payment testing", priority: TaskPriority.High,
            assignmentStatus: TaskAssignmentStatus.Accepted,
            workStatus: TaskWorkStatus.InProgress);
        await SeedTaskAsync(fixture, "Payment documentation", priority: TaskPriority.Low,
            assignmentStatus: TaskAssignmentStatus.Accepted, workStatus: TaskWorkStatus.InProgress);
        await SeedTaskAsync(fixture, "Payment queued", priority: TaskPriority.High,
            assignmentStatus: TaskAssignmentStatus.Pending, workStatus: TaskWorkStatus.ToDo);

        var tasks = await fixture.CreateStore(fixture.Creator).LoadCreatedAsync(new TaskListQuery(
            Search: "payment",
            WorkStatus: TaskWorkStatus.InProgress,
            AssignmentStatus: TaskAssignmentStatus.Accepted,
            Priority: TaskPriority.High));

        Assert.Equal(expected.Id, Assert.Single(tasks).Id);
    }

    [Fact]
    public async Task Filters_NeverReturnUnrelatedTasks()
    {
        var fixture = await TestFixture.CreateAsync();
        var own = await SeedTaskAsync(fixture, "Matching task", priority: TaskPriority.Urgent);
        await SeedTaskAsync(
            fixture, "Matching unrelated task", priority: TaskPriority.Urgent,
            creator: fixture.Assignee, assignee: fixture.Unrelated);

        var tasks = await fixture.CreateStore(fixture.Creator).LoadCreatedAsync(new TaskListQuery(
            Search: "matching", Priority: TaskPriority.Urgent));

        Assert.Equal(own.Id, Assert.Single(tasks).Id);
    }

    [Fact]
    public async Task DeadlineSort_OrdersNearestFirst()
    {
        var fixture = await TestFixture.CreateAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var later = await SeedTaskAsync(fixture, "Later", deadline: today.AddDays(5));
        var sooner = await SeedTaskAsync(fixture, "Sooner", deadline: today.AddDays(1));

        var tasks = await fixture.CreateStore(fixture.Creator).LoadCreatedAsync(
            new TaskListQuery(Sort: TaskSortOrder.DeadlineNearest));

        Assert.Equal([sooner.Id, later.Id], tasks.Select(task => task.Id));
    }

    [Fact]
    public async Task PrioritySort_OrdersHighestFirst()
    {
        var fixture = await TestFixture.CreateAsync();
        var low = await SeedTaskAsync(fixture, "Low", priority: TaskPriority.Low);
        var urgent = await SeedTaskAsync(fixture, "Urgent", priority: TaskPriority.Urgent);
        var medium = await SeedTaskAsync(fixture, "Medium", priority: TaskPriority.Medium);

        var tasks = await fixture.CreateStore(fixture.Creator).LoadCreatedAsync(
            new TaskListQuery(Sort: TaskSortOrder.PriorityHighest));

        Assert.Equal([urgent.Id, medium.Id, low.Id], tasks.Select(task => task.Id));
    }

    [Fact]
    public async Task NewestSort_OrdersMostRecentlyCreatedFirst()
    {
        var fixture = await TestFixture.CreateAsync();
        var old = await SeedTaskAsync(fixture, "Old", createdAt: DateTime.UtcNow.AddDays(-2));
        var newest = await SeedTaskAsync(fixture, "Newest", createdAt: DateTime.UtcNow);
        var middle = await SeedTaskAsync(fixture, "Middle", createdAt: DateTime.UtcNow.AddDays(-1));

        var tasks = await fixture.CreateStore(fixture.Creator).LoadCreatedAsync(
            new TaskListQuery(Sort: TaskSortOrder.Newest));

        Assert.Equal([newest.Id, middle.Id, old.Id], tasks.Select(task => task.Id));
    }

    [Fact]
    public async Task UnifiedList_ContainsTaskAssignedToCurrentUser()
    {
        var fixture = await TestFixture.CreateAsync();
        var assigned = await SeedTaskAsync(
            fixture, "Assigned to current", creator: fixture.Assignee, assignee: fixture.Creator);

        var tasks = await fixture.CreateStore(fixture.Creator).LoadRelatedAsync();

        var result = Assert.Single(tasks);
        Assert.Equal(assigned.Id, result.Id);
        Assert.True(result.IsAssignedToCurrentUser);
        Assert.False(result.IsCreatedByCurrentUser);
    }

    [Fact]
    public async Task UnifiedList_ContainsTaskCreatedByCurrentUser()
    {
        var fixture = await TestFixture.CreateAsync();
        var created = await SeedTaskAsync(fixture, "Created by current");

        var tasks = await fixture.CreateStore(fixture.Creator).LoadRelatedAsync();

        var result = Assert.Single(tasks);
        Assert.Equal(created.Id, result.Id);
        Assert.True(result.IsCreatedByCurrentUser);
        Assert.False(result.IsAssignedToCurrentUser);
    }

    [Fact]
    public async Task AllTasks_IncludesTasksUnrelatedToCurrentUser()
    {
        var fixture = await TestFixture.CreateAsync();
        var unrelated = await SeedTaskAsync(
            fixture, "Unrelated", creator: fixture.Assignee, assignee: fixture.Unrelated);

        var tasks = await fixture.CreateStore(fixture.Creator).LoadRelatedAsync();

        var result = Assert.Single(tasks);
        Assert.Equal(unrelated.Id, result.Id);
        Assert.False(result.IsCreatedByCurrentUser);
        Assert.False(result.IsAssignedToCurrentUser);
        Assert.False(result.CanManageWorkStatus);
    }

    [Fact]
    public async Task MeAssigneeFilter_ReturnsOnlyTasksAssignedToCurrentUser()
    {
        var fixture = await TestFixture.CreateAsync();
        var assigned = await SeedTaskAsync(
            fixture, "Assigned to me", creator: fixture.Assignee, assignee: fixture.Creator);
        await SeedTaskAsync(fixture, "Created by me", assignee: fixture.Assignee);
        await SeedTaskAsync(
            fixture, "Other company task", creator: fixture.Assignee, assignee: fixture.Unrelated);

        var tasks = await fixture.CreateStore(fixture.Creator).LoadRelatedAsync(
            new(AssigneeIds: [fixture.Creator.Id]));

        Assert.Equal(assigned.Id, Assert.Single(tasks).Id);
        Assert.True(tasks[0].IsAssignedToCurrentUser);
    }

    [Fact]
    public async Task MultiAssigneeFilter_UsesOrLogicWithoutDuplicates()
    {
        var fixture = await TestFixture.CreateAsync();
        var mine = await SeedTaskAsync(
            fixture, "Mine", creator: fixture.Assignee, assignee: fixture.Creator);
        var doers = await SeedTaskAsync(
            fixture, "Assignee task", creator: fixture.Creator, assignee: fixture.Assignee);
        await SeedTaskAsync(
            fixture, "Unrelated assignee", creator: fixture.Assignee, assignee: fixture.Unrelated);
        var store = fixture.CreateStore(fixture.Creator);

        var tasks = await store.LoadRelatedAsync(new(
            AssigneeIds: [fixture.Creator.Id, fixture.Assignee.Id, fixture.Creator.Id]));

        Assert.Equal(2, tasks.Count);
        Assert.Equal(2, tasks.Select(task => task.Id).Distinct().Count());
        Assert.Contains(tasks, task => task.Id == mine.Id);
        Assert.Contains(tasks, task => task.Id == doers.Id);
    }

    [Fact]
    public async Task Unassigned_CanBeCombinedWithSelectedAssignees()
    {
        var fixture = await TestFixture.CreateAsync();
        var assigned = await SeedTaskAsync(
            fixture, "For selected user", assignee: fixture.Assignee);
        var unassigned = await SeedTaskAsync(fixture, "No assignee", unassigned: true);
        await SeedTaskAsync(fixture, "Different assignee", assignee: fixture.Unrelated);
        await fixture.CreateStore(fixture.Creator).CreateAsync(NewEmailRequest("invited-filter@example.com"));

        var tasks = await fixture.CreateStore(fixture.Creator).LoadRelatedAsync(
            new(AssigneeIds: [fixture.Assignee.Id], IncludeUnassigned: true));

        Assert.Equal(2, tasks.Count);
        Assert.Contains(tasks, task => task.Id == assigned.Id);
        Assert.Contains(tasks, task => task.Id == unassigned.Id);
        Assert.DoesNotContain(tasks, task => task.IsInvited);
    }

    [Fact]
    public async Task EmptyAssigneeSelection_ReturnsAllCompanyTasks()
    {
        var fixture = await TestFixture.CreateAsync();
        var first = await SeedTaskAsync(fixture, "First", assignee: fixture.Assignee);
        var second = await SeedTaskAsync(
            fixture, "Second", creator: fixture.Assignee, assignee: fixture.Unrelated);

        var tasks = await fixture.CreateStore(fixture.Creator).LoadRelatedAsync(
            new(AssigneeIds: []));

        Assert.Equal(2, tasks.Count);
        Assert.Contains(tasks, task => task.Id == first.Id);
        Assert.Contains(tasks, task => task.Id == second.Id);
    }

    [Fact]
    public async Task MultiAssigneeFilter_CombinesWithPriorityAndWorkStatus()
    {
        var fixture = await TestFixture.CreateAsync();
        var expected = await SeedTaskAsync(
            fixture,
            "Matching",
            priority: TaskPriority.High,
            workStatus: TaskWorkStatus.InProgress,
            creator: fixture.Assignee,
            assignee: fixture.Creator);
        await SeedTaskAsync(
            fixture,
            "Wrong priority",
            priority: TaskPriority.Low,
            workStatus: TaskWorkStatus.InProgress,
            creator: fixture.Assignee,
            assignee: fixture.Creator);
        await SeedTaskAsync(
            fixture,
            "Wrong relation",
            priority: TaskPriority.High,
            workStatus: TaskWorkStatus.InProgress);

        var tasks = await fixture.CreateStore(fixture.Creator).LoadRelatedAsync(new(
            WorkStatus: TaskWorkStatus.InProgress,
            Priority: TaskPriority.High,
            AssigneeIds: [fixture.Creator.Id, fixture.Unrelated.Id]));

        Assert.Equal(expected.Id, Assert.Single(tasks).Id);
    }

    [Fact]
    public async Task Search_WorksAcrossUnifiedDataset()
    {
        var fixture = await TestFixture.CreateAsync();
        var expected = await SeedTaskAsync(fixture, "Launch research");
        await SeedTaskAsync(
            fixture, "Budget review", creator: fixture.Assignee, assignee: fixture.Creator);

        var tasks = await fixture.CreateStore(fixture.Creator).LoadRelatedAsync(new(Search: "LAUNCH"));

        Assert.Equal(expected.Id, Assert.Single(tasks).Id);
    }

    [Fact]
    public async Task UnifiedVisibility_DoesNotGrantMakerWorkStatusPermission()
    {
        var fixture = await TestFixture.CreateAsync();
        var task = await SeedTaskAsync(
            fixture,
            "Visible but not draggable",
            assignmentStatus: TaskAssignmentStatus.Accepted,
            creator: fixture.Creator,
            assignee: fixture.Assignee);
        var makerStore = fixture.CreateStore(fixture.Creator);

        var summary = Assert.Single(await makerStore.LoadRelatedAsync());
        Assert.False(summary.CanManageWorkStatus);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => makerStore.ChangeWorkStatusAsync(
            task.Id, new(TaskWorkStatus.InProgress, task.Version)));
    }

    [Fact]
    public async Task Task_CanBeCreatedWithoutProject()
    {
        var fixture = await TestFixture.CreateAsync();

        var id = await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));

        await using var db = fixture.CreateDbContext();
        Assert.Null((await db.Tasks.SingleAsync(task => task.Id == id)).ProjectId);
    }

    [Fact]
    public async Task Task_CanBeCreatedWithExistingProject()
    {
        var fixture = await TestFixture.CreateAsync();
        var project = await SeedProjectAsync(fixture, "LUMA Calendar");
        var request = NewRequest(fixture.Assignee.Id) with { ProjectId = project.Id };

        var id = await fixture.CreateStore(fixture.Creator).CreateAsync(request);

        await using var db = fixture.CreateDbContext();
        Assert.Equal(project.Id, (await db.Tasks.SingleAsync(task => task.Id == id)).ProjectId);
        Assert.Equal("LUMA Calendar", (await fixture.CreateStore(fixture.Creator).LoadDetailsAsync(id)).ProjectName);
        Assert.Equal("LUMA Calendar", Assert.Single(fixture.Notifier.CreatedNotifications).ProjectName);
    }

    [Fact]
    public async Task Maker_CanChangeAndRemoveProject()
    {
        var fixture = await TestFixture.CreateAsync();
        var first = await SeedProjectAsync(fixture, "UNIAP");
        var second = await SeedProjectAsync(fixture, "LUMA Calendar");
        var task = await SeedTaskAsync(fixture, "Move project", project: first);
        var store = fixture.CreateStore(fixture.Creator);

        var changed = await store.UpdateContentAsync(task.Id, new(task.Title, task.Description, task.Version, task.Priority, second.Id));
        var removed = await store.UpdateContentAsync(task.Id, new(changed.Title, changed.Description, changed.Version, changed.Priority, null));

        Assert.Equal(second.Id, changed.ProjectId);
        Assert.Null(removed.ProjectId);
        Assert.Equal(string.Empty, removed.ProjectName);
    }

    [Fact]
    public async Task Doer_CannotChangeProject()
    {
        var fixture = await TestFixture.CreateAsync();
        var project = await SeedProjectAsync(fixture, "Protected");
        var task = await SeedTaskAsync(fixture, "Protected task");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.CreateStore(fixture.Assignee).UpdateContentAsync(
                task.Id, new(task.Title, task.Description, task.Version, task.Priority, project.Id)));
    }

    [Fact]
    public async Task ProjectChange_PreservesLifecycleDeadlineRequestAndComments()
    {
        var fixture = await TestFixture.CreateAsync();
        var project = await SeedProjectAsync(fixture, "Preservation");
        var task = await SeedTaskAsync(
            fixture, "Preserve state", assignmentStatus: TaskAssignmentStatus.DeadlineChangeRequested,
            workStatus: TaskWorkStatus.InProgress);
        var requested = DateOnly.FromDateTime(DateTime.Today.AddDays(12));
        var acceptedAt = DateTime.UtcNow.AddDays(-1);
        await using (var db = fixture.CreateDbContext())
        {
            var entity = await db.Tasks.SingleAsync(item => item.Id == task.Id);
            entity.AcceptedAt = acceptedAt;
            entity.RequestedDeadline = requested;
            entity.DeadlineChangeComment = "Need vendor time";
            entity.DeadlineChangeRequestedAt = DateTime.UtcNow;
            db.TaskComments.Add(new LumaTaskComment { TaskId = entity.Id, AuthorUserId = fixture.Creator.Id, Text = "Keep me", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            task.Version = entity.Version;
        }

        await fixture.CreateStore(fixture.Creator).UpdateContentAsync(
            task.Id, new(task.Title, task.Description, task.Version, task.Priority, project.Id));

        await using var verify = fixture.CreateDbContext();
        var saved = await verify.Tasks.Include(item => item.Comments).SingleAsync(item => item.Id == task.Id);
        Assert.Equal(TaskAssignmentStatus.DeadlineChangeRequested, saved.AssignmentStatus);
        Assert.Equal(TaskWorkStatus.InProgress, saved.WorkStatus);
        Assert.Equal(task.Deadline, saved.Deadline);
        Assert.Equal(acceptedAt, saved.AcceptedAt);
        Assert.Equal(requested, saved.RequestedDeadline);
        Assert.Equal("Need vendor time", saved.DeadlineChangeComment);
        Assert.Equal("Keep me", Assert.Single(saved.Comments).Text);
    }

    [Fact]
    public async Task ProjectTaskQuery_ReturnsOnlySelectedProjectAndCombinesFilters()
    {
        var fixture = await TestFixture.CreateAsync();
        var selected = await SeedProjectAsync(fixture, "Selected");
        var other = await SeedProjectAsync(fixture, "Other");
        var expected = await SeedTaskAsync(fixture, "Launch research", priority: TaskPriority.High,
            assignmentStatus: TaskAssignmentStatus.Accepted, workStatus: TaskWorkStatus.InProgress, project: selected);
        await SeedTaskAsync(fixture, "Wrong priority", priority: TaskPriority.Low,
            assignmentStatus: TaskAssignmentStatus.Accepted, workStatus: TaskWorkStatus.InProgress, project: selected);
        await SeedTaskAsync(fixture, "Wrong project", priority: TaskPriority.High,
            assignmentStatus: TaskAssignmentStatus.Accepted, workStatus: TaskWorkStatus.InProgress, project: other);

        var tasks = await fixture.CreateStore(fixture.Unrelated).LoadProjectTasksAsync(selected.Id, new(
            Search: "LAUNCH", WorkStatus: TaskWorkStatus.InProgress,
            AssignmentStatus: TaskAssignmentStatus.Accepted, Priority: TaskPriority.High,
            AssigneeIds: [fixture.Assignee.Id]));

        Assert.Equal(expected.Id, Assert.Single(tasks).Id);
    }

    [Fact]
    public async Task ProjectBoardQuery_ColumnCountsReflectOnlyFilteredProjectTasks()
    {
        var fixture = await TestFixture.CreateAsync();
        var selected = await SeedProjectAsync(fixture, "Selected Board");
        var other = await SeedProjectAsync(fixture, "Other Board");
        await SeedTaskAsync(fixture, "Selected todo", priority: TaskPriority.High,
            assignmentStatus: TaskAssignmentStatus.Accepted, workStatus: TaskWorkStatus.ToDo, project: selected);
        await SeedTaskAsync(fixture, "Selected doing", priority: TaskPriority.High,
            assignmentStatus: TaskAssignmentStatus.Accepted, workStatus: TaskWorkStatus.InProgress, project: selected);
        await SeedTaskAsync(fixture, "Filtered priority", priority: TaskPriority.Low,
            assignmentStatus: TaskAssignmentStatus.Accepted, workStatus: TaskWorkStatus.Done, project: selected);
        await SeedTaskAsync(fixture, "Wrong project", priority: TaskPriority.High,
            assignmentStatus: TaskAssignmentStatus.Accepted, workStatus: TaskWorkStatus.Done, project: other);

        var tasks = await fixture.CreateStore(fixture.Unrelated).LoadProjectTasksAsync(
            selected.Id, new TaskListQuery(Priority: TaskPriority.High));

        Assert.Equal(2, tasks.Count);
        Assert.Single(tasks, task => task.WorkStatus == TaskWorkStatus.ToDo);
        Assert.Single(tasks, task => task.WorkStatus == TaskWorkStatus.InProgress);
        Assert.DoesNotContain(tasks, task => task.WorkStatus == TaskWorkStatus.Done);
        Assert.All(tasks, task => Assert.Equal(selected.Id, task.ProjectId));
    }

    [Fact]
    public async Task ProjectBoardStatusChange_ReusesDoerAuthorizationAndPersists()
    {
        var fixture = await TestFixture.CreateAsync();
        var project = await SeedProjectAsync(fixture, "Delivery Board");
        var task = await SeedTaskAsync(fixture, "Start project work",
            assignmentStatus: TaskAssignmentStatus.Accepted, workStatus: TaskWorkStatus.ToDo, project: project);
        var doerStore = fixture.CreateStore(fixture.Assignee);

        var updated = await doerStore.ChangeWorkStatusAsync(
            task.Id, new ChangeTaskWorkStatusRequest(TaskWorkStatus.InProgress, task.Version));
        var projectTask = Assert.Single(await doerStore.LoadProjectTasksAsync(project.Id));

        Assert.Equal(TaskWorkStatus.InProgress, updated.WorkStatus);
        Assert.Equal(TaskWorkStatus.InProgress, projectTask.WorkStatus);
        Assert.True(projectTask.CanManageWorkStatus);
    }

    [Fact]
    public async Task ProjectVisibility_DoesNotGrantTaskMutation()
    {
        var fixture = await TestFixture.CreateAsync();
        var project = await SeedProjectAsync(fixture, "Shared");
        var task = await SeedTaskAsync(fixture, "Visible task", assignmentStatus: TaskAssignmentStatus.Accepted, project: project);
        var unrelatedStore = fixture.CreateStore(fixture.Unrelated);

        Assert.Equal(task.Id, Assert.Single(await unrelatedStore.LoadProjectTasksAsync(project.Id)).Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            unrelatedStore.ChangeWorkStatusAsync(task.Id, new(TaskWorkStatus.InProgress, task.Version)));
    }

    [Fact]
    public async Task ProjectChange_DoesNotSendEmail()
    {
        var fixture = await TestFixture.CreateAsync();
        var project = await SeedProjectAsync(fixture, "LUMA Calendar");
        var task = await SeedTaskAsync(fixture, "Notify project");

        await fixture.CreateStore(fixture.Creator).UpdateContentAsync(
            task.Id, new(task.Title, task.Description, task.Version, task.Priority, project.Id));

        Assert.Empty(fixture.Notifier.UpdatedNotifications);
    }

    [Fact]
    public async Task TaskActivity_CreatesInboxItemsOnlyForTheOtherParty()
    {
        var fixture = await TestFixture.CreateAsync();
        var makerStore = fixture.CreateStore(fixture.Creator);
        var doerStore = fixture.CreateStore(fixture.Assignee);
        var taskId = await makerStore.CreateAsync(NewRequest(fixture.Assignee.Id));

        await doerStore.AcceptAsync(taskId);
        var accepted = await doerStore.LoadDetailsAsync(taskId);
        await makerStore.UpdateContentAsync(taskId, new(
            "Updated launch notes", accepted.Description, accepted.Version, accepted.Priority, accepted.ProjectId));
        var updated = await doerStore.LoadDetailsAsync(taskId);
        await doerStore.ChangeWorkStatusAsync(taskId, new(TaskWorkStatus.InProgress, updated.Version));
        await makerStore.AddCommentAsync(taskId, new("Please check the new scope."));

        await using var db = fixture.CreateDbContext();
        var items = await db.InboxItems.OrderBy(item => item.CreatedAt).ToListAsync();
        Assert.Collection(items,
            item => AssertInbox(item, InboxActivityType.TaskAssigned, fixture.Creator, fixture.Assignee, taskId),
            item => AssertInbox(item, InboxActivityType.TaskAccepted, fixture.Assignee, fixture.Creator, taskId),
            item => AssertInbox(item, InboxActivityType.TaskUpdated, fixture.Creator, fixture.Assignee, taskId),
            item => AssertInbox(item, InboxActivityType.WorkStatusChanged, fixture.Assignee, fixture.Creator, taskId),
            item => AssertInbox(item, InboxActivityType.CommentAdded, fixture.Creator, fixture.Assignee, taskId));
        Assert.DoesNotContain(items, item => item.RecipientUserId == fixture.Unrelated.Id);
    }

    [Fact]
    public async Task DeadlineActions_CreateInboxItemsForTheReviewerAndDoer()
    {
        var fixture = await TestFixture.CreateAsync();
        var makerStore = fixture.CreateStore(fixture.Creator);
        var doerStore = fixture.CreateStore(fixture.Assignee);
        var declinedTaskId = await makerStore.CreateAsync(NewRequest(fixture.Assignee.Id) with { Title = "Declined request" });
        await doerStore.RequestDeadlineChangeAsync(declinedTaskId, NewDeadlineRequest());
        await makerStore.DeclineDeadlineChangeAsync(declinedTaskId);

        var approvedTaskId = await makerStore.CreateAsync(NewRequest(fixture.Assignee.Id) with { Title = "Approved request" });
        await doerStore.RequestDeadlineChangeAsync(approvedTaskId, NewDeadlineRequest(12));
        await makerStore.ApproveDeadlineChangeAsync(approvedTaskId);

        await using var db = fixture.CreateDbContext();
        var deadlineItems = await db.InboxItems
            .Where(item => item.ActivityType == InboxActivityType.DeadlineChangeRequested ||
                           item.ActivityType == InboxActivityType.DeadlineChangeApproved ||
                           item.ActivityType == InboxActivityType.DeadlineChangeDeclined)
            .ToListAsync();
        Assert.Equal(4, deadlineItems.Count);
        Assert.Equal(2, deadlineItems.Count(item =>
            item.ActivityType == InboxActivityType.DeadlineChangeRequested &&
            item.RecipientUserId == fixture.Creator.Id));
        Assert.Contains(deadlineItems, item =>
            item.ActivityType == InboxActivityType.DeadlineChangeApproved &&
            item.RecipientUserId == fixture.Assignee.Id);
        Assert.Contains(deadlineItems, item =>
            item.ActivityType == InboxActivityType.DeadlineChangeDeclined &&
            item.RecipientUserId == fixture.Assignee.Id);
    }

    [Fact]
    public async Task TakingUnassignedTask_CreatesInboxForMakerButSendsNoEmail()
    {
        var fixture = await TestFixture.CreateAsync();
        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(
            NewRequest(fixture.Assignee.Id) with { AssigneeId = null, Deadline = null });
        var details = await fixture.CreateStore(fixture.Unrelated).LoadDetailsAsync(taskId);

        await fixture.CreateStore(fixture.Unrelated).TakeAsync(taskId, new(details.Version));

        await using var db = fixture.CreateDbContext();
        var item = Assert.Single(await db.InboxItems.ToListAsync());
        AssertInbox(item, InboxActivityType.TaskTaken, fixture.Unrelated, fixture.Creator, taskId);
        Assert.Empty(fixture.Notifier.CreatedNotifications);
        Assert.Empty(fixture.Notifier.AcceptedNotifications);
    }

    [Fact]
    public async Task SelfAssignedTask_DoesNotCreateInboxItems()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Creator.Id));
        await store.AcceptAsync(taskId);
        await store.AddCommentAsync(taskId, new("Personal note"));

        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.InboxItems.ToListAsync());
    }

    [Fact]
    public async Task InboxStore_LoadsRecentAndMarksOneOrAllAsRead()
    {
        var fixture = await TestFixture.CreateAsync();
        var makerStore = fixture.CreateStore(fixture.Creator);
        var taskId = await makerStore.CreateAsync(NewRequest(fixture.Assignee.Id));
        await fixture.CreateStore(fixture.Assignee).AcceptAsync(taskId);
        await makerStore.UpdateContentAsync(taskId, new(
            "Inbox update", "Description", (await makerStore.LoadDetailsAsync(taskId)).Version));

        var assigneeInbox = fixture.CreateInboxStore(fixture.Assignee);
        var badgeRefreshes = 0;
        assigneeInbox.Changed += () => badgeRefreshes++;
        var initial = await assigneeInbox.LoadRecentAsync();
        Assert.Equal(2, initial.UnreadCount);
        Assert.Equal(2, await assigneeInbox.GetUnreadCountAsync());
        Assert.All(initial.Items, item => Assert.False(item.IsRead));
        Assert.All(initial.Items, item => Assert.Equal(taskId, item.TaskId));

        Assert.True(await assigneeInbox.MarkReadAsync(initial.Items[0].Id));
        var afterOne = await assigneeInbox.LoadRecentAsync();
        Assert.Equal(1, afterOne.UnreadCount);
        Assert.Single(afterOne.Items, item => item.IsRead);

        Assert.Equal(1, await assigneeInbox.MarkAllReadAsync());
        var afterAll = await assigneeInbox.LoadRecentAsync();
        Assert.Equal(0, afterAll.UnreadCount);
        Assert.All(afterAll.Items, item => Assert.True(item.IsRead));
        Assert.Equal(2, badgeRefreshes);
    }

    [Fact]
    public async Task InboxStore_CannotMarkAnotherUsersItemRead()
    {
        var fixture = await TestFixture.CreateAsync();
        await fixture.CreateStore(fixture.Creator).CreateAsync(NewRequest(fixture.Assignee.Id));
        Guid itemId;
        await using (var db = fixture.CreateDbContext())
            itemId = (await db.InboxItems.SingleAsync()).Id;

        Assert.False(await fixture.CreateInboxStore(fixture.Unrelated).MarkReadAsync(itemId));

        await using var verify = fixture.CreateDbContext();
        Assert.Null((await verify.InboxItems.SingleAsync()).ReadAt);
    }

    [Fact]
    public async Task Task_CanLinkMultipleProjectFeatures_AndDetailsReturnChips()
    {
        var fixture = await TestFixture.CreateAsync();
        var project = await SeedProjectAsync(fixture, "LUMA");
        var calendar = await SeedFeatureAsync(fixture, project, "Calendar");
        var sharing = await SeedFeatureAsync(fixture, project, "Sharing");

        var taskId = await fixture.CreateStore(fixture.Creator).CreateAsync(
            NewRequest(fixture.Assignee.Id) with
            {
                ProjectId = project.Id,
                FeatureIds = [calendar.Id, sharing.Id]
            });

        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, await db.TaskFeatures.CountAsync(item => item.TaskId == taskId));
        var details = await fixture.CreateStore(fixture.Unrelated).LoadDetailsAsync(taskId);
        Assert.Equal(["Calendar", "Sharing"], details.Features!.Select(item => item.Name));
    }

    [Fact]
    public async Task SameFeature_CanLinkTaskAndBug()
    {
        var fixture = await TestFixture.CreateAsync();
        var project = await SeedProjectAsync(fixture, "Product");
        var feature = await SeedFeatureAsync(fixture, project, "Checkout");
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id) with { ProjectId = project.Id, FeatureIds = [feature.Id] });
        var bugId = await store.CreateAsync(NewRequest(fixture.Assignee.Id) with
        {
            Title = "Checkout fails",
            ProjectId = project.Id,
            FeatureIds = [feature.Id],
            WorkItemType = WorkItemType.Bug,
            BugCategory = BugCategory.Functional,
            BugSeverity = BugSeverity.High,
            BugReproducibility = BugReproducibility.Always
        });

        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, await db.TaskFeatures.CountAsync(item => item.FeatureId == feature.Id));
        Assert.Equal(new[] { taskId, bugId }.Order(), (await db.TaskFeatures.Where(item => item.FeatureId == feature.Id).Select(item => item.TaskId).ToListAsync()).Order());
    }

    [Fact]
    public async Task FeatureSelection_RejectsNoProjectAndCrossProjectLinks()
    {
        var fixture = await TestFixture.CreateAsync();
        var first = await SeedProjectAsync(fixture, "First");
        var second = await SeedProjectAsync(fixture, "Second");
        var feature = await SeedFeatureAsync(fixture, first, "First feature");
        var store = fixture.CreateStore(fixture.Creator);

        await Assert.ThrowsAsync<ValidationException>(() => store.CreateAsync(
            NewRequest(fixture.Assignee.Id) with { FeatureIds = [feature.Id] }));
        await Assert.ThrowsAsync<ValidationException>(() => store.CreateAsync(
            NewRequest(fixture.Assignee.Id) with { ProjectId = second.Id, FeatureIds = [feature.Id] }));
    }

    [Fact]
    public async Task ChangingProjectWithoutFeatureSelection_ClearsOldRelations()
    {
        var fixture = await TestFixture.CreateAsync();
        var first = await SeedProjectAsync(fixture, "First");
        var second = await SeedProjectAsync(fixture, "Second");
        var feature = await SeedFeatureAsync(fixture, first, "Legacy");
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id) with
        {
            ProjectId = first.Id,
            FeatureIds = [feature.Id]
        });
        var before = await store.LoadDetailsAsync(taskId);

        var updated = await store.UpdateContentAsync(taskId, new UpdateLumaTaskContentRequest(
            before.Title, before.Description, before.Version, before.Priority, second.Id,
            FeatureIds: null));

        Assert.Equal(second.Id, updated.ProjectId);
        Assert.Empty(updated.Features!);
        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.TaskFeatures.Where(item => item.TaskId == taskId).ToListAsync());
    }

    [Fact]
    public async Task FeatureFilter_UsesOrWithinFeatures_AndAndWithOtherFilters()
    {
        var fixture = await TestFixture.CreateAsync();
        var project = await SeedProjectAsync(fixture, "Filtered");
        var alpha = await SeedFeatureAsync(fixture, project, "Alpha");
        var beta = await SeedFeatureAsync(fixture, project, "Beta");
        var store = fixture.CreateStore(fixture.Creator);
        var alphaHigh = await store.CreateAsync(NewRequest(fixture.Assignee.Id) with { Title = "Alpha high", ProjectId = project.Id, Priority = TaskPriority.High, FeatureIds = [alpha.Id] });
        var betaHigh = await store.CreateAsync(NewRequest(fixture.Assignee.Id) with { Title = "Beta high", ProjectId = project.Id, Priority = TaskPriority.High, FeatureIds = [beta.Id] });
        _ = await store.CreateAsync(NewRequest(fixture.Assignee.Id) with { Title = "Alpha low", ProjectId = project.Id, Priority = TaskPriority.Low, FeatureIds = [alpha.Id] });

        var results = await store.LoadRelatedAsync(new TaskListQuery(
            Priority: TaskPriority.High,
            ProjectId: project.Id,
            FeatureIds: [alpha.Id, beta.Id]));

        Assert.Equal(new[] { alphaHigh, betaHigh }.Order(), results.Select(item => item.Id).Order());
    }

    [Fact]
    public async Task EditingFeatures_WritesCompactAddedAndRemovedChangeLogs()
    {
        var fixture = await TestFixture.CreateAsync();
        var project = await SeedProjectAsync(fixture, "Logged");
        var oldFeature = await SeedFeatureAsync(fixture, project, "Old");
        var newFeature = await SeedFeatureAsync(fixture, project, "New");
        var store = fixture.CreateStore(fixture.Creator);
        var taskId = await store.CreateAsync(NewRequest(fixture.Assignee.Id) with { ProjectId = project.Id, FeatureIds = [oldFeature.Id] });
        var before = await store.LoadDetailsAsync(taskId);

        await store.UpdateContentAsync(taskId, new UpdateLumaTaskContentRequest(
            before.Title, before.Description, before.Version, before.Priority, before.ProjectId,
            FeatureIds: [newFeature.Id]));

        await using var db = fixture.CreateDbContext();
        var logs = await db.TaskChangeLogs.Where(item => item.TaskId == taskId &&
            (item.ChangeType == TaskChangeType.FeatureAdded || item.ChangeType == TaskChangeType.FeatureRemoved)).ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.Contains(logs, item => item.ChangeType == TaskChangeType.FeatureAdded && item.NewValue == newFeature.Id.ToString("D"));
        Assert.Contains(logs, item => item.ChangeType == TaskChangeType.FeatureRemoved && item.OldValue == oldFeature.Id.ToString("D"));
        Assert.All(logs, item => Assert.Equal(fixture.Creator.Id, item.ActorUserId));
        Assert.Single(logs.Select(item => item.MutationId).Distinct());
    }

    private static void AssertInbox(
        InboxItem item,
        InboxActivityType activityType,
        AppUser actor,
        AppUser recipient,
        Guid taskId)
    {
        Assert.Equal(activityType, item.ActivityType);
        Assert.Equal(actor.Id, item.ActorUserId);
        Assert.Equal(recipient.Id, item.RecipientUserId);
        Assert.Equal(taskId, item.TaskId);
        Assert.False(string.IsNullOrWhiteSpace(item.Message));
        Assert.Null(item.ReadAt);
    }

    private static TaskAttachmentUpload ImageUpload(
        string fileName,
        string contentType,
        byte[] bytes,
        long? declaredLength = null) =>
        new(fileName, contentType, declaredLength ?? bytes.LongLength,
            () => new MemoryStream(bytes, writable: false));

    private static byte[] PngBytes(params byte[] payload) =>
        [137, 80, 78, 71, 13, 10, 26, 10, .. payload];

    private static byte[] WebpBytes() =>
        [82, 73, 70, 70, 4, 0, 0, 0, 87, 69, 66, 80, 86, 80, 56, 32];

    private static CreateLumaTaskRequest NewRequest(Guid assigneeId) => new(
        "  Prepare launch notes  ",
        "  Include the final checklist.  ",
        assigneeId,
        DateOnly.FromDateTime(DateTime.Today.AddDays(7)));

    private static CreateLumaTaskRequest NewEmailRequest(string email) => new(
        "  Prepare launch notes  ",
        "  Include the final checklist.  ",
        null,
        DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
        TaskPriority.None,
        email);

    private static string InvitationToken(string invitationUrl)
    {
        var uri = new Uri(invitationUrl);
        return QueryHelpers.ParseQuery(uri.Query)["token"].Single()!;
    }

    private static RequestTaskDeadlineChange NewDeadlineRequest(int daysFromToday = 10) => new(
        DateOnly.FromDateTime(DateTime.Today.AddDays(daysFromToday)),
        "Waiting for the vendor.");

    private static async Task<LumaTask> SeedTaskAsync(
        TestFixture fixture,
        string title,
        DateOnly? deadline = null,
        TaskPriority priority = TaskPriority.None,
        TaskAssignmentStatus assignmentStatus = TaskAssignmentStatus.Pending,
        TaskWorkStatus workStatus = TaskWorkStatus.ToDo,
        DateTime? createdAt = null,
        AppUser? creator = null,
        AppUser? assignee = null,
        LumaProject? project = null,
        bool unassigned = false,
        bool noDeadline = false)
    {
        var task = new LumaTask
        {
            Title = title,
            Description = string.Empty,
            CreatorId = (creator ?? fixture.Creator).Id,
            AssigneeId = unassigned ? null : (assignee ?? fixture.Assignee).Id,
            ProjectId = project?.Id,
            Deadline = noDeadline ? null : deadline ?? DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            CreatedAt = createdAt ?? DateTime.UtcNow,
            Priority = priority,
            AssignmentStatus = assignmentStatus,
            WorkStatus = workStatus,
            AcceptedAt = assignmentStatus == TaskAssignmentStatus.Accepted ? DateTime.UtcNow : null,
            Version = Guid.NewGuid()
        };
        await using var db = fixture.CreateDbContext();
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<LumaProject> SeedProjectAsync(TestFixture fixture, string name)
    {
        var project = new LumaProject
        {
            Name = name,
            Description = string.Empty,
            CreatedByUserId = fixture.Creator.Id,
            CreatedAt = DateTime.UtcNow,
            Version = Guid.NewGuid()
        };
        await using var db = fixture.CreateDbContext();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    private static async Task<LumaFeature> SeedFeatureAsync(TestFixture fixture, LumaProject project, string name)
    {
        var feature = new LumaFeature
        {
            ProjectId = project.Id,
            Name = name,
            NormalizedName = name.Trim().ToUpperInvariant(),
            Description = string.Empty,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = fixture.Creator.Id
        };
        await using var db = fixture.CreateDbContext();
        db.Features.Add(feature);
        await db.SaveChangesAsync();
        return feature;
    }

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
        public RecordingTaskAttachmentStorage AttachmentStorage { get; } = new();

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
                AttachmentStorage,
                NullLogger<TaskStore>.Instance);
        public InboxStore CreateInboxStore(AppUser user) =>
            new(new TestDbContextFactory(Options), new TestAuthenticationStateProvider(user));

        public static AppUser NewUser(string email, string name) => new()
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
        public string Invitation(string token) => $"https://luma.test/task-invitation?token={Uri.EscapeDataString(token)}";
    }

    private sealed class RecordingTaskAttachmentStorage : ITaskAttachmentStorage
    {
        private readonly Dictionary<string, byte[]> files = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, byte[]> Files => files;

        public async Task SaveAsync(
            string storageKey,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            await using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            files.Add(storageKey, buffer.ToArray());
        }

        public Task<Stream> OpenReadAsync(
            string storageKey,
            CancellationToken cancellationToken = default)
        {
            if (!files.TryGetValue(storageKey, out var content))
                throw new FileNotFoundException("The attachment does not exist.", storageKey);

            return Task.FromResult<Stream>(new MemoryStream(content, writable: false));
        }

        public Task DeleteAsync(
            string storageKey,
            CancellationToken cancellationToken = default)
        {
            files.Remove(storageKey);
            return Task.CompletedTask;
        }
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
