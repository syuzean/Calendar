using Calendar.Models;
using Calendar.Services.Email;
using Xunit;

namespace Calendar.Tests;

public sealed class TaskEmailNotifierTests
{
    [Fact]
    public async Task TaskCreated_SendsOnlyToTaskDoer()
    {
        var sender = new RecordingEmailSender();
        var notifier = new TaskEmailNotifier(sender, Renderer());

        await notifier.NotifyCreatedAsync(CreatedNotification(
        [
            new("Maker", "maker@luma.test", TaskNotificationRole.Maker),
            new("Doer", "doer@luma.test", TaskNotificationRole.Doer)
        ]));

        var doer = Assert.Single(sender.Messages);
        Assert.Equal("doer@luma.test", doer.RecipientAddress);
        Assert.Contains("assigned to you", doer.PlainTextBody);
        Assert.Contains("Open task in LUMA", doer.HtmlBody);
        Assert.Contains("https://luma.test/tasks?task=", doer.PlainTextBody);
    }

    [Fact]
    public async Task TaskAccepted_SendsOnlyToTaskMaker()
    {
        var sender = new RecordingEmailSender();
        var notifier = new TaskEmailNotifier(sender, Renderer());

        await notifier.NotifyAcceptedAsync(AcceptedNotification(
        [
            new("Maker", "maker@luma.test", TaskNotificationRole.Maker),
            new("Doer", "doer@luma.test", TaskNotificationRole.Doer)
        ]));

        var maker = Assert.Single(sender.Messages);
        Assert.Equal("maker@luma.test", maker.RecipientAddress);
        Assert.Contains("Doer accepted your task", maker.PlainTextBody);
        Assert.Contains("✓ Accepted", maker.HtmlBody);
    }

    [Fact]
    public async Task SelfAssignedTask_ProducesNoEmail()
    {
        var sender = new RecordingEmailSender();
        var notifier = new TaskEmailNotifier(sender, Renderer());
        TaskNotificationRecipient[] recipients =
        [
            new("Self", "self@luma.test", TaskNotificationRole.Maker),
            new("Self", "self@luma.test", TaskNotificationRole.Doer)
        ];

        await notifier.NotifyCreatedAsync(CreatedNotification(recipients));
        await notifier.NotifyAcceptedAsync(AcceptedNotification(recipients));
        await notifier.NotifyDeadlineChangeRequestedAsync(DeadlineRequestedNotification(recipients));
        await notifier.NotifyDeadlineChangeApprovedAsync(DeadlineApprovedNotification(recipients));
        await notifier.NotifyDeadlineChangeDeclinedAsync(DeadlineDeclinedNotification(recipients));
        await notifier.NotifyUpdatedAsync(UpdatedNotification(recipients));
        await notifier.NotifyWorkStatusChangedAsync(WorkStatusNotification(recipients));
        await notifier.NotifyCommentAddedAsync(CommentNotification(TaskNotificationRole.Maker, recipients));

        Assert.Empty(sender.Messages);
    }

    [Fact]
    public async Task DuplicateEmailAddresses_AreNormalizedAndNotNotifiedTwice()
    {
        var sender = new RecordingEmailSender();
        var notifier = new TaskEmailNotifier(sender, Renderer());

        await notifier.NotifyCreatedAsync(CreatedNotification(
        [
            new("First", " Same@Luma.Test ", TaskNotificationRole.Doer),
            new("Second", "same@luma.test", TaskNotificationRole.Doer)
        ]));

        Assert.Single(sender.Messages);
        Assert.Equal("Same@Luma.Test", sender.Messages[0].RecipientAddress);
    }

    [Fact]
    public async Task DeadlineRequest_NotifiesOnlyMaker()
    {
        var sender = new RecordingEmailSender();
        var notifier = new TaskEmailNotifier(sender, Renderer());

        await notifier.NotifyDeadlineChangeRequestedAsync(DeadlineRequestedNotification(BothRecipients()));

        var message = Assert.Single(sender.Messages);
        Assert.Equal("maker@luma.test", message.RecipientAddress);
        Assert.Contains("requested a deadline change", message.PlainTextBody);
        Assert.Contains("Waiting for the vendor", message.HtmlBody);
    }

    [Fact]
    public async Task DeadlineApprovalAndDecline_NotifyOnlyDoer()
    {
        var sender = new RecordingEmailSender();
        var notifier = new TaskEmailNotifier(sender, Renderer());

        await notifier.NotifyDeadlineChangeApprovedAsync(DeadlineApprovedNotification(BothRecipients()));
        await notifier.NotifyDeadlineChangeDeclinedAsync(DeadlineDeclinedNotification(BothRecipients()));

        Assert.Equal(2, sender.Messages.Count);
        Assert.All(sender.Messages, message => Assert.Equal("doer@luma.test", message.RecipientAddress));
        Assert.Contains(sender.Messages, message => message.Subject.Contains("approved", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(sender.Messages, message => message.Subject.Contains("declined", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TaskUpdated_SendsOnlyChangedFieldsToDoer()
    {
        var sender = new RecordingEmailSender();
        var notifier = new TaskEmailNotifier(sender, Renderer());

        await notifier.NotifyUpdatedAsync(UpdatedNotification(BothRecipients()));

        var message = Assert.Single(sender.Messages);
        Assert.Equal("doer@luma.test", message.RecipientAddress);
        Assert.Contains("Payment testing", message.PlainTextBody);
        Assert.Contains("Payment + refund testing", message.HtmlBody);
        Assert.DoesNotContain("Description", message.PlainTextBody);
    }

    [Fact]
    public async Task TaskUpdated_RendersPriorityChangeForDoer()
    {
        var sender = new RecordingEmailSender();
        var notifier = new TaskEmailNotifier(sender, Renderer());
        var notification = UpdatedNotification(BothRecipients()) with
        {
            Changes = new TaskContentChanges(
                false, "", "", false, "", "",
                true, TaskPriority.Medium, TaskPriority.High)
        };

        await notifier.NotifyUpdatedAsync(notification);

        var message = Assert.Single(sender.Messages);
        Assert.Equal("doer@luma.test", message.RecipientAddress);
        Assert.Contains("Priority", message.PlainTextBody);
        Assert.Contains("Medium", message.PlainTextBody);
        Assert.Contains("High", message.HtmlBody);
        Assert.DoesNotContain("Description", message.PlainTextBody);
    }

    [Theory]
    [InlineData(TaskWorkStatus.InProgress, "started", "In Progress")]
    [InlineData(TaskWorkStatus.Done, "completed", "Done")]
    public async Task WorkStatusChange_SendsOnlyToMaker(
        TaskWorkStatus newStatus, string actionText, string statusLabel)
    {
        var sender = new RecordingEmailSender();
        var notifier = new TaskEmailNotifier(sender, Renderer());
        var notification = WorkStatusNotification(BothRecipients()) with { NewStatus = newStatus };

        await notifier.NotifyWorkStatusChangedAsync(notification);

        var message = Assert.Single(sender.Messages);
        Assert.Equal("maker@luma.test", message.RecipientAddress);
        Assert.Contains(actionText, message.PlainTextBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(statusLabel, message.HtmlBody);
    }

    [Theory]
    [InlineData(TaskNotificationRole.Maker, "doer@luma.test")]
    [InlineData(TaskNotificationRole.Doer, "maker@luma.test")]
    public async Task TaskCommentAdded_NotifiesOnlyOtherParty(
        TaskNotificationRole authorRole, string expectedRecipient)
    {
        var sender = new RecordingEmailSender();
        var notifier = new TaskEmailNotifier(sender, Renderer());

        await notifier.NotifyCommentAddedAsync(CommentNotification(authorRole, BothRecipients()));

        var message = Assert.Single(sender.Messages);
        Assert.Equal(expectedRecipient, message.RecipientAddress);
        Assert.Contains("A useful comment", message.PlainTextBody);
        Assert.Contains("Comment Author", message.HtmlBody);
        Assert.Contains("Open task in LUMA", message.HtmlBody);
    }

    [Fact]
    public void TaskCommentTemplate_EncodesUserContentInHtml()
    {
        var rendered = Renderer().RenderTaskCommentAdded(new TaskCommentAddedTemplateData(
            "Recipient", "recipient@luma.test", "<Task>", "<Author>",
            "<script>alert('x')</script>\nSecond line", "https://luma.test/tasks?task=1"));

        Assert.DoesNotContain("<script>", rendered.HtmlBody);
        Assert.Contains("&lt;script&gt;", rendered.HtmlBody);
        Assert.Contains("<br>", rendered.HtmlBody);
        Assert.Contains("<script>", rendered.PlainTextBody);
    }

    private static TaskCreatedNotification CreatedNotification(IReadOnlyList<TaskNotificationRecipient> recipients) => new(
        "Prepare launch notes",
        "Include the final checklist.",
        "Maker",
        "Doer",
        new DateOnly(2026, 9, 4),
        "https://luma.test/tasks?task=11111111-1111-1111-1111-111111111111",
        recipients);

    private static TaskAcceptedNotification AcceptedNotification(IReadOnlyList<TaskNotificationRecipient> recipients) => new(
        "Prepare launch notes",
        "Maker",
        "Doer",
        new DateOnly(2026, 9, 4),
        new DateTime(2026, 8, 25, 14, 40, 0, DateTimeKind.Utc),
        "https://luma.test/tasks?task=11111111-1111-1111-1111-111111111111",
        recipients);

    private static TaskDeadlineChangeRequestedNotification DeadlineRequestedNotification(IReadOnlyList<TaskNotificationRecipient> recipients) => new(
        "Prepare launch notes", "Maker", "Doer", new DateOnly(2026, 9, 4), new DateOnly(2026, 9, 8),
        "Waiting for the vendor.", new DateTime(2026, 8, 25, 15, 0, 0, DateTimeKind.Utc),
        "https://luma.test/tasks?task=11111111-1111-1111-1111-111111111111", recipients);

    private static TaskDeadlineChangeApprovedNotification DeadlineApprovedNotification(IReadOnlyList<TaskNotificationRecipient> recipients) => new(
        "Prepare launch notes", "Maker", "Doer", new DateOnly(2026, 9, 4), new DateOnly(2026, 9, 8),
        "Waiting for the vendor.", new DateTime(2026, 8, 25, 16, 0, 0, DateTimeKind.Utc),
        "https://luma.test/tasks?task=11111111-1111-1111-1111-111111111111", recipients);

    private static TaskDeadlineChangeDeclinedNotification DeadlineDeclinedNotification(IReadOnlyList<TaskNotificationRecipient> recipients) => new(
        "Prepare launch notes", "Maker", "Doer", new DateOnly(2026, 9, 4), new DateOnly(2026, 9, 8),
        "Waiting for the vendor.", new DateTime(2026, 8, 25, 16, 0, 0, DateTimeKind.Utc),
        "https://luma.test/tasks?task=11111111-1111-1111-1111-111111111111", recipients);

    private static TaskUpdatedNotification UpdatedNotification(IReadOnlyList<TaskNotificationRecipient> recipients) => new(
        "Payment + refund testing", "Maker", "Doer", new DateOnly(2026, 9, 4),
        new TaskContentChanges(
            true, "Payment testing", "Payment + refund testing",
            false, "", "",
            false, TaskPriority.None, TaskPriority.None),
        "https://luma.test/tasks?task=11111111-1111-1111-1111-111111111111", recipients);

    private static TaskWorkStatusChangedNotification WorkStatusNotification(IReadOnlyList<TaskNotificationRecipient> recipients) => new(
        "Payment testing", "Maker", "Doer", TaskWorkStatus.ToDo, TaskWorkStatus.InProgress,
        new DateOnly(2026, 9, 4),
        "https://luma.test/tasks?task=11111111-1111-1111-1111-111111111111", recipients);

    private static TaskCommentAddedNotification CommentNotification(
        TaskNotificationRole authorRole,
        IReadOnlyList<TaskNotificationRecipient> recipients) => new(
        "Prepare launch notes", "Comment Author", "A useful comment.", authorRole,
        "https://luma.test/tasks?task=11111111-1111-1111-1111-111111111111", recipients);

    private static TaskNotificationRecipient[] BothRecipients() =>
    [
        new("Maker", "maker@luma.test", TaskNotificationRole.Maker),
        new("Doer", "doer@luma.test", TaskNotificationRole.Doer)
    ];

    private static FileEmailTemplateRenderer Renderer() =>
        new(Path.Combine(AppContext.BaseDirectory, "EmailTemplates"));

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<EmailMessage> Messages { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
