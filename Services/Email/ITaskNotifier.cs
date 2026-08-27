using Calendar.Models;

namespace Calendar.Services.Email;

public interface ITaskNotifier
{
    Task NotifyCreatedAsync(TaskCreatedNotification notification, CancellationToken cancellationToken = default);
    Task NotifyAcceptedAsync(TaskAcceptedNotification notification, CancellationToken cancellationToken = default);
    Task NotifyDeadlineChangeRequestedAsync(TaskDeadlineChangeRequestedNotification notification, CancellationToken cancellationToken = default);
    Task NotifyDeadlineChangeApprovedAsync(TaskDeadlineChangeApprovedNotification notification, CancellationToken cancellationToken = default);
    Task NotifyDeadlineChangeDeclinedAsync(TaskDeadlineChangeDeclinedNotification notification, CancellationToken cancellationToken = default);
    Task NotifyUpdatedAsync(TaskUpdatedNotification notification, CancellationToken cancellationToken = default);
    Task NotifyWorkStatusChangedAsync(TaskWorkStatusChangedNotification notification, CancellationToken cancellationToken = default);
    Task NotifyCommentAddedAsync(TaskCommentAddedNotification notification, CancellationToken cancellationToken = default);
}

public enum TaskNotificationRole
{
    Maker,
    Doer
}

public sealed record TaskNotificationRecipient(
    string Name,
    string Email,
    TaskNotificationRole Role);

public sealed record TaskCreatedNotification(
    string TaskTitle,
    string Description,
    string MakerName,
    string DoerName,
    DateOnly Deadline,
    TaskPriority Priority,
    string TaskUrl,
    IReadOnlyList<TaskNotificationRecipient> Recipients,
    string ProjectName = "");

public sealed record TaskAcceptedNotification(
    string TaskTitle,
    string MakerName,
    string DoerName,
    DateOnly Deadline,
    DateTime AcceptedAt,
    string TaskUrl,
    IReadOnlyList<TaskNotificationRecipient> Recipients,
    string ProjectName = "");

public sealed record TaskDeadlineChangeRequestedNotification(
    string TaskTitle,
    string MakerName,
    string DoerName,
    DateOnly CurrentDeadline,
    DateOnly RequestedDeadline,
    string Comment,
    DateTime RequestedAt,
    string TaskUrl,
    IReadOnlyList<TaskNotificationRecipient> Recipients,
    string ProjectName = "");

public sealed record TaskDeadlineChangeApprovedNotification(
    string TaskTitle,
    string MakerName,
    string DoerName,
    DateOnly PreviousDeadline,
    DateOnly ApprovedDeadline,
    string Comment,
    DateTime ApprovedAt,
    string TaskUrl,
    IReadOnlyList<TaskNotificationRecipient> Recipients,
    string ProjectName = "");

public sealed record TaskDeadlineChangeDeclinedNotification(
    string TaskTitle,
    string MakerName,
    string DoerName,
    DateOnly CurrentDeadline,
    DateOnly DeclinedDeadline,
    string Comment,
    DateTime DeclinedAt,
    string TaskUrl,
    IReadOnlyList<TaskNotificationRecipient> Recipients,
    string ProjectName = "");

public sealed record TaskContentChanges(
    bool TitleChanged,
    string PreviousTitle,
    string UpdatedTitle,
    bool DescriptionChanged,
    string PreviousDescription,
    string UpdatedDescription,
    bool PriorityChanged,
    TaskPriority PreviousPriority,
    TaskPriority UpdatedPriority,
    bool ProjectChanged = false,
    string PreviousProject = "",
    string UpdatedProject = "");

public sealed record TaskUpdatedNotification(
    string TaskTitle,
    string MakerName,
    string DoerName,
    DateOnly Deadline,
    TaskContentChanges Changes,
    string TaskUrl,
    IReadOnlyList<TaskNotificationRecipient> Recipients,
    string ProjectName = "");

public sealed record TaskWorkStatusChangedNotification(
    string TaskTitle,
    string MakerName,
    string DoerName,
    TaskWorkStatus PreviousStatus,
    TaskWorkStatus NewStatus,
    DateOnly Deadline,
    string TaskUrl,
    IReadOnlyList<TaskNotificationRecipient> Recipients,
    string ProjectName = "");

public sealed record TaskCommentAddedNotification(
    string TaskTitle,
    string CommentAuthor,
    string CommentText,
    TaskNotificationRole AuthorRole,
    string TaskUrl,
    IReadOnlyList<TaskNotificationRecipient> Recipients,
    string ProjectName = "");
