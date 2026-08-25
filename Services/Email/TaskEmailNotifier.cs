using Calendar.Models;

namespace Calendar.Services.Email;

public sealed class TaskEmailNotifier(
    IEmailSender emailSender,
    ITaskEmailTemplateRenderer templateRenderer) : ITaskNotifier
{
    public Task NotifyCreatedAsync(
        TaskCreatedNotification notification,
        CancellationToken cancellationToken = default) => SendAllAsync(
        notification.Recipients,
        TaskNotificationRole.Doer,
        recipient => templateRenderer.RenderTaskCreated(new TaskCreatedTemplateData(
            recipient.Name,
            recipient.Email,
            "A new task has been assigned to you.",
            notification.TaskTitle,
            notification.Description,
            notification.MakerName,
            notification.DoerName,
            notification.Deadline,
            notification.TaskUrl)),
        cancellationToken);

    public Task NotifyAcceptedAsync(
        TaskAcceptedNotification notification,
        CancellationToken cancellationToken = default) => SendAllAsync(
        notification.Recipients,
        TaskNotificationRole.Maker,
        recipient => templateRenderer.RenderTaskAccepted(new TaskAcceptedTemplateData(
            recipient.Name,
            recipient.Email,
            $"{notification.DoerName} accepted your task.",
            notification.TaskTitle,
            notification.DoerName,
            notification.Deadline,
            notification.AcceptedAt,
            notification.TaskUrl)),
        cancellationToken);

    public Task NotifyDeadlineChangeRequestedAsync(
        TaskDeadlineChangeRequestedNotification notification,
        CancellationToken cancellationToken = default) => SendAllAsync(
        notification.Recipients,
        TaskNotificationRole.Maker,
        recipient => templateRenderer.RenderTaskDeadlineChangeRequested(new TaskDeadlineChangeRequestedTemplateData(
            recipient.Name,
            recipient.Email,
            notification.TaskTitle,
            notification.MakerName,
            notification.DoerName,
            notification.CurrentDeadline,
            notification.RequestedDeadline,
            notification.Comment,
            notification.RequestedAt,
            notification.TaskUrl)),
        cancellationToken);

    public Task NotifyDeadlineChangeApprovedAsync(
        TaskDeadlineChangeApprovedNotification notification,
        CancellationToken cancellationToken = default) => SendAllAsync(
        notification.Recipients,
        TaskNotificationRole.Doer,
        recipient => templateRenderer.RenderTaskDeadlineChangeApproved(new TaskDeadlineChangeApprovedTemplateData(
            recipient.Name,
            recipient.Email,
            notification.TaskTitle,
            notification.MakerName,
            notification.DoerName,
            notification.PreviousDeadline,
            notification.ApprovedDeadline,
            notification.Comment,
            notification.ApprovedAt,
            notification.TaskUrl)),
        cancellationToken);

    public Task NotifyDeadlineChangeDeclinedAsync(
        TaskDeadlineChangeDeclinedNotification notification,
        CancellationToken cancellationToken = default) => SendAllAsync(
        notification.Recipients,
        TaskNotificationRole.Doer,
        recipient => templateRenderer.RenderTaskDeadlineChangeDeclined(new TaskDeadlineChangeDeclinedTemplateData(
            recipient.Name,
            recipient.Email,
            notification.TaskTitle,
            notification.MakerName,
            notification.DoerName,
            notification.CurrentDeadline,
            notification.DeclinedDeadline,
            notification.Comment,
            notification.DeclinedAt,
            notification.TaskUrl)),
        cancellationToken);

    public Task NotifyUpdatedAsync(
        TaskUpdatedNotification notification,
        CancellationToken cancellationToken = default) => SendAllAsync(
        notification.Recipients,
        TaskNotificationRole.Doer,
        recipient => templateRenderer.RenderTaskUpdated(new TaskUpdatedTemplateData(
            recipient.Name,
            recipient.Email,
            notification.TaskTitle,
            notification.MakerName,
            notification.DoerName,
            notification.Deadline,
            notification.Changes.TitleChanged,
            notification.Changes.PreviousTitle,
            notification.Changes.UpdatedTitle,
            notification.Changes.DescriptionChanged,
            notification.Changes.PreviousDescription,
            notification.Changes.UpdatedDescription,
            notification.TaskUrl)),
        cancellationToken);

    public Task NotifyWorkStatusChangedAsync(
        TaskWorkStatusChangedNotification notification,
        CancellationToken cancellationToken = default) => SendAllAsync(
        notification.Recipients,
        TaskNotificationRole.Maker,
        recipient => templateRenderer.RenderTaskWorkStatusChanged(new TaskWorkStatusChangedTemplateData(
            recipient.Name,
            recipient.Email,
            notification.NewStatus == TaskWorkStatus.Done ? "Task completed" : "Task started",
            notification.NewStatus == TaskWorkStatus.Done
                ? $"{notification.DoerName} completed \"{notification.TaskTitle}\"."
                : $"{notification.DoerName} started working on \"{notification.TaskTitle}\".",
            notification.TaskTitle,
            notification.DoerName,
            WorkStatusLabel(notification.PreviousStatus),
            WorkStatusLabel(notification.NewStatus),
            notification.Deadline,
            notification.TaskUrl)),
        cancellationToken);

    public Task NotifyCommentAddedAsync(
        TaskCommentAddedNotification notification,
        CancellationToken cancellationToken = default) => SendAllAsync(
        notification.Recipients,
        notification.AuthorRole == TaskNotificationRole.Maker
            ? TaskNotificationRole.Doer
            : TaskNotificationRole.Maker,
        recipient => templateRenderer.RenderTaskCommentAdded(new TaskCommentAddedTemplateData(
            recipient.Name,
            recipient.Email,
            notification.TaskTitle,
            notification.CommentAuthor,
            notification.CommentText,
            notification.TaskUrl)),
        cancellationToken);

    private async Task SendAllAsync(
        IReadOnlyList<TaskNotificationRecipient> recipients,
        TaskNotificationRole targetRole,
        Func<TaskNotificationRecipient, RenderedEmailTemplate> render,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        foreach (var group in recipients
                     .Where(recipient => !string.IsNullOrWhiteSpace(recipient.Email))
                     .GroupBy(recipient => NormalizeEmail(recipient.Email), StringComparer.Ordinal))
        {
            var recipient = group.First();
            var isMaker = group.Any(item => item.Role == TaskNotificationRole.Maker);
            var isDoer = group.Any(item => item.Role == TaskNotificationRole.Doer);
            if ((isMaker && isDoer) || !group.Any(item => item.Role == targetRole)) continue;

            var rendered = render(recipient);
            try
            {
                await emailSender.SendAsync(new EmailMessage(
                    recipient.Email.Trim(),
                    rendered.Subject,
                    rendered.PlainTextBody,
                    rendered.HtmlBody), cancellationToken);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
            throw new AggregateException("One or more task notification emails could not be sent.", failures);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    private static string WorkStatusLabel(TaskWorkStatus status) => status switch
    {
        TaskWorkStatus.ToDo => "To Do",
        TaskWorkStatus.InProgress => "In Progress",
        TaskWorkStatus.Done => "Done",
        _ => status.ToString()
    };
}
