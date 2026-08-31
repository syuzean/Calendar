using Calendar.Models;

namespace Calendar.Services.Email;

public interface ITaskEmailTemplateRenderer
{
    RenderedEmailTemplate RenderTaskCreated(TaskCreatedTemplateData data);
    RenderedEmailTemplate RenderTaskAccepted(TaskAcceptedTemplateData data);
    RenderedEmailTemplate RenderTaskDeadlineChangeRequested(TaskDeadlineChangeRequestedTemplateData data);
    RenderedEmailTemplate RenderTaskDeadlineChangeApproved(TaskDeadlineChangeApprovedTemplateData data);
    RenderedEmailTemplate RenderTaskDeadlineChangeDeclined(TaskDeadlineChangeDeclinedTemplateData data);
    RenderedEmailTemplate RenderTaskUpdated(TaskUpdatedTemplateData data);
    RenderedEmailTemplate RenderTaskWorkStatusChanged(TaskWorkStatusChangedTemplateData data);
    RenderedEmailTemplate RenderTaskCommentAdded(TaskCommentAddedTemplateData data);
}

public sealed record TaskCreatedTemplateData(
    string RecipientName,
    string RecipientEmail,
    string IntroText,
    string TaskTitle,
    string Description,
    string TaskMaker,
    string TaskDoer,
    DateOnly? Deadline,
    TaskPriority Priority,
    string TaskUrl,
    string ProjectName = "");

public sealed record TaskAcceptedTemplateData(
    string RecipientName,
    string RecipientEmail,
    string IntroText,
    string TaskTitle,
    string TaskDoer,
    DateOnly? Deadline,
    DateTime AcceptedAt,
    string TaskUrl,
    string ProjectName = "");

public sealed record TaskDeadlineChangeRequestedTemplateData(
    string RecipientName,
    string RecipientEmail,
    string TaskTitle,
    string TaskMaker,
    string TaskDoer,
    DateOnly CurrentDeadline,
    DateOnly RequestedDeadline,
    string Comment,
    DateTime RequestedAt,
    string TaskUrl,
    string ProjectName = "");

public sealed record TaskDeadlineChangeApprovedTemplateData(
    string RecipientName,
    string RecipientEmail,
    string TaskTitle,
    string TaskMaker,
    string TaskDoer,
    DateOnly PreviousDeadline,
    DateOnly ApprovedDeadline,
    string Comment,
    DateTime ApprovedAt,
    string TaskUrl,
    string ProjectName = "");

public sealed record TaskDeadlineChangeDeclinedTemplateData(
    string RecipientName,
    string RecipientEmail,
    string TaskTitle,
    string TaskMaker,
    string TaskDoer,
    DateOnly CurrentDeadline,
    DateOnly DeclinedDeadline,
    string Comment,
    DateTime DeclinedAt,
    string TaskUrl,
    string ProjectName = "");

public sealed record TaskUpdatedTemplateData(
    string RecipientName,
    string RecipientEmail,
    string TaskTitle,
    string TaskMaker,
    string TaskDoer,
    DateOnly? Deadline,
    bool TitleChanged,
    string PreviousTitle,
    string UpdatedTitle,
    bool DescriptionChanged,
    string PreviousDescription,
    string UpdatedDescription,
    bool PriorityChanged,
    string PreviousPriority,
    string UpdatedPriority,
    bool ProjectChanged,
    string PreviousProject,
    string UpdatedProject,
    string TaskUrl,
    string ProjectName = "");

public sealed record TaskWorkStatusChangedTemplateData(
    string RecipientName,
    string RecipientEmail,
    string SubjectLabel,
    string ActionText,
    string TaskTitle,
    string TaskDoer,
    string PreviousStatus,
    string NewStatus,
    DateOnly? Deadline,
    string TaskUrl,
    string ProjectName = "");

public sealed record TaskCommentAddedTemplateData(
    string RecipientName,
    string RecipientEmail,
    string TaskTitle,
    string CommentAuthor,
    string CommentText,
    string TaskUrl,
    string ProjectName = "");
