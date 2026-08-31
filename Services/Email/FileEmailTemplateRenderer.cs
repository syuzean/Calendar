using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Calendar.Models;

namespace Calendar.Services.Email;

public sealed partial class FileEmailTemplateRenderer(string templateDirectory) : IEmailTemplateRenderer, ITaskEmailTemplateRenderer
{
    public RenderedEmailTemplate RenderEventShared(EventSharedTemplateData data)
    {
        var values = EventValues(
            data.RecipientName, data.RecipientEmail, data.OrganizerName, data.EventTitle,
            data.Start, data.End, data.IsAllDay, data.Description, data.EventColor,
            data.EventUrl, data.MeetingUrl, data.MeetingProvider);
        return RenderTemplate("EventShared", values);
    }

    public RenderedEmailTemplate RenderEventUpdated(EventUpdatedTemplateData data)
    {
        var values = EventValues(
            data.RecipientName, data.RecipientEmail, data.OrganizerName, data.EventTitle,
            data.Start, data.End, data.IsAllDay, data.Description, data.EventColor,
            data.EventUrl, data.MeetingUrl, data.MeetingProvider);
        values["ChangedFields"] = data.ChangedFields;
        return RenderTemplate("EventUpdated", values);
    }

    public RenderedEmailTemplate RenderEventCancelled(EventCancelledTemplateData data)
    {
        var values = EventValues(
            data.RecipientName, data.RecipientEmail, data.OrganizerName, data.EventTitle,
            data.Start, data.End, data.IsAllDay, string.Empty, data.EventColor,
            string.Empty, string.Empty, string.Empty);
        return RenderTemplate("EventCancelled", values);
    }

    public RenderedEmailTemplate RenderTaskCreated(TaskCreatedTemplateData data)
    {
        var values = TaskValues(
            data.RecipientName,
            data.RecipientEmail,
            data.IntroText,
            data.TaskTitle,
            data.Description,
            data.TaskMaker,
            data.TaskDoer,
            data.Deadline,
            data.TaskUrl,
            null,
            data.ProjectName);
        values["Priority"] = data.Priority == TaskPriority.None ? "No priority" : data.Priority.ToString();
        return RenderTemplate("TaskCreated", values);
    }

    public RenderedEmailTemplate RenderTaskAccepted(TaskAcceptedTemplateData data) =>
        RenderTemplate("TaskAccepted", TaskValues(
            data.RecipientName,
            data.RecipientEmail,
            data.IntroText,
            data.TaskTitle,
            string.Empty,
            string.Empty,
            data.TaskDoer,
            data.Deadline,
            data.TaskUrl,
            data.AcceptedAt,
            data.ProjectName));

    public RenderedEmailTemplate RenderTaskDeadlineChangeRequested(TaskDeadlineChangeRequestedTemplateData data) =>
        RenderTemplate("TaskDeadlineChangeRequested", DeadlineChangeValues(
            data.RecipientName, data.RecipientEmail, data.TaskTitle, data.TaskMaker, data.TaskDoer,
            data.CurrentDeadline, data.RequestedDeadline, data.Comment, data.RequestedAt, data.TaskUrl, data.ProjectName));

    public RenderedEmailTemplate RenderTaskDeadlineChangeApproved(TaskDeadlineChangeApprovedTemplateData data) =>
        RenderTemplate("TaskDeadlineChangeApproved", DeadlineChangeValues(
            data.RecipientName, data.RecipientEmail, data.TaskTitle, data.TaskMaker, data.TaskDoer,
            data.PreviousDeadline, data.ApprovedDeadline, data.Comment, data.ApprovedAt, data.TaskUrl, data.ProjectName));

    public RenderedEmailTemplate RenderTaskDeadlineChangeDeclined(TaskDeadlineChangeDeclinedTemplateData data) =>
        RenderTemplate("TaskDeadlineChangeDeclined", DeadlineChangeValues(
            data.RecipientName, data.RecipientEmail, data.TaskTitle, data.TaskMaker, data.TaskDoer,
            data.CurrentDeadline, data.DeclinedDeadline, data.Comment, data.DeclinedAt, data.TaskUrl, data.ProjectName));

    public RenderedEmailTemplate RenderTaskUpdated(TaskUpdatedTemplateData data) =>
        RenderTemplate("TaskUpdated", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RecipientName"] = string.IsNullOrWhiteSpace(data.RecipientName) ? data.RecipientEmail : data.RecipientName,
            ["RecipientEmail"] = data.RecipientEmail,
            ["TaskTitle"] = data.TaskTitle,
            ["TaskMaker"] = data.TaskMaker,
            ["TaskDoer"] = data.TaskDoer,
            ["Deadline"] = FormatTaskDeadline(data.Deadline),
            ["TitleChanged"] = data.TitleChanged ? "true" : string.Empty,
            ["PreviousTitle"] = data.PreviousTitle,
            ["UpdatedTitle"] = data.UpdatedTitle,
            ["DescriptionChanged"] = data.DescriptionChanged ? "true" : string.Empty,
            ["PreviousDescription"] = DisplayDescription(data.PreviousDescription),
            ["UpdatedDescription"] = DisplayDescription(data.UpdatedDescription),
            ["PriorityChanged"] = data.PriorityChanged ? "true" : string.Empty,
            ["PreviousPriority"] = data.PreviousPriority,
            ["UpdatedPriority"] = data.UpdatedPriority,
            ["ProjectChanged"] = data.ProjectChanged ? "true" : string.Empty,
            ["PreviousProject"] = data.PreviousProject,
            ["UpdatedProject"] = data.UpdatedProject,
            ["Project"] = data.ProjectName,
            ["HasProject"] = string.IsNullOrWhiteSpace(data.ProjectName) ? string.Empty : "true",
            ["TaskUrl"] = data.TaskUrl
        });

    public RenderedEmailTemplate RenderTaskWorkStatusChanged(TaskWorkStatusChangedTemplateData data) =>
        RenderTemplate("TaskStatusChanged", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RecipientName"] = string.IsNullOrWhiteSpace(data.RecipientName) ? data.RecipientEmail : data.RecipientName,
            ["RecipientEmail"] = data.RecipientEmail,
            ["SubjectLabel"] = data.SubjectLabel,
            ["ActionText"] = data.ActionText,
            ["TaskTitle"] = data.TaskTitle,
            ["TaskDoer"] = data.TaskDoer,
            ["PreviousStatus"] = data.PreviousStatus,
            ["NewStatus"] = data.NewStatus,
            ["Deadline"] = FormatTaskDeadline(data.Deadline),
            ["Project"] = data.ProjectName,
            ["HasProject"] = string.IsNullOrWhiteSpace(data.ProjectName) ? string.Empty : "true",
            ["TaskUrl"] = data.TaskUrl
        });

    public RenderedEmailTemplate RenderTaskCommentAdded(TaskCommentAddedTemplateData data) =>
        RenderTemplate("TaskCommentAdded", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RecipientName"] = string.IsNullOrWhiteSpace(data.RecipientName) ? data.RecipientEmail : data.RecipientName,
            ["RecipientEmail"] = data.RecipientEmail,
            ["TaskTitle"] = data.TaskTitle,
            ["CommentAuthor"] = data.CommentAuthor,
            ["CommentText"] = data.CommentText,
            ["Project"] = data.ProjectName,
            ["HasProject"] = string.IsNullOrWhiteSpace(data.ProjectName) ? string.Empty : "true",
            ["TaskUrl"] = data.TaskUrl
        });

    private RenderedEmailTemplate RenderTemplate(string templateName, IReadOnlyDictionary<string, string> values)
    {
        var subject = Render(Read($"{templateName}.subject.txt"), values, htmlEncode: false)
            .Replace('\r', ' ').Replace('\n', ' ').Trim();
        var html = Render(Read($"{templateName}.html"), values, htmlEncode: true).Trim();
        var text = Render(Read($"{templateName}.txt"), values, htmlEncode: false).Trim();
        return new RenderedEmailTemplate(subject, html, text);
    }

    private string Read(string fileName) => File.ReadAllText(Path.Combine(templateDirectory, fileName));

    private static Dictionary<string, string> EventValues(
        string recipientName,
        string recipientEmail,
        string organizerName,
        string eventTitle,
        DateTime start,
        DateTime end,
        bool isAllDay,
        string description,
        string eventColor,
        string eventUrl,
        string meetingUrl,
        string meetingProvider)
    {
        var finalDate = isAllDay ? end.AddDays(-1) : end;
        var eventDate = start.Date == finalDate.Date
            ? start.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture)
            : $"{start:MMMM d, yyyy} – {finalDate:MMMM d, yyyy}";
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RecipientName"] = string.IsNullOrWhiteSpace(recipientName) ? recipientEmail : recipientName,
            ["RecipientEmail"] = recipientEmail,
            ["OrganizerName"] = organizerName,
            ["EventTitle"] = eventTitle,
            ["EventDate"] = eventDate,
            ["StartTime"] = isAllDay ? "All day" : start.ToString("h:mm tt", CultureInfo.InvariantCulture),
            ["EndTime"] = isAllDay ? "" : end.ToString("h:mm tt", CultureInfo.InvariantCulture),
            ["Description"] = description ?? string.Empty,
            ["EventColor"] = eventColor,
            ["EventUrl"] = eventUrl,
            ["MeetingUrl"] = meetingUrl,
            ["MeetingProvider"] = meetingProvider
        };
    }

    private static Dictionary<string, string> TaskValues(
        string recipientName,
        string recipientEmail,
        string introText,
        string taskTitle,
        string description,
        string taskMaker,
        string taskDoer,
        DateOnly? deadline,
        string taskUrl,
        DateTime? acceptedAt,
        string projectName) => new(StringComparer.Ordinal)
    {
        ["RecipientName"] = string.IsNullOrWhiteSpace(recipientName) ? recipientEmail : recipientName,
        ["RecipientEmail"] = recipientEmail,
        ["IntroText"] = introText,
        ["TaskTitle"] = taskTitle,
        ["Description"] = description ?? string.Empty,
        ["TaskMaker"] = taskMaker,
        ["TaskDoer"] = taskDoer,
        ["Deadline"] = FormatTaskDeadline(deadline),
        ["Project"] = projectName ?? string.Empty,
        ["HasProject"] = string.IsNullOrWhiteSpace(projectName) ? string.Empty : "true",
        ["TaskUrl"] = taskUrl,
        ["AcceptedAt"] = acceptedAt?.ToString("MMMM d, yyyy 'at' h:mm tt", CultureInfo.InvariantCulture) ?? string.Empty
    };

    private static Dictionary<string, string> DeadlineChangeValues(
        string recipientName,
        string recipientEmail,
        string taskTitle,
        string taskMaker,
        string taskDoer,
        DateOnly currentDeadline,
        DateOnly requestedDeadline,
        string comment,
        DateTime actionAt,
        string taskUrl,
        string projectName) => new(StringComparer.Ordinal)
    {
        ["RecipientName"] = string.IsNullOrWhiteSpace(recipientName) ? recipientEmail : recipientName,
        ["RecipientEmail"] = recipientEmail,
        ["TaskTitle"] = taskTitle,
        ["TaskMaker"] = taskMaker,
        ["TaskDoer"] = taskDoer,
        ["CurrentDeadline"] = currentDeadline.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture),
        ["RequestedDeadline"] = requestedDeadline.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture),
        ["Comment"] = comment ?? string.Empty,
        ["Project"] = projectName ?? string.Empty,
        ["HasProject"] = string.IsNullOrWhiteSpace(projectName) ? string.Empty : "true",
        ["ActionAt"] = actionAt.ToString("MMMM d, yyyy 'at' h:mm tt", CultureInfo.InvariantCulture),
        ["TaskUrl"] = taskUrl
    };

    private static string DisplayDescription(string value) =>
        string.IsNullOrWhiteSpace(value) ? "No description" : value;

    private static string FormatTaskDeadline(DateOnly? deadline) =>
        deadline?.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture) ?? "No deadline";

    private static string Render(string template, IReadOnlyDictionary<string, string> values, bool htmlEncode)
    {
        template = ConditionalPattern().Replace(template, match =>
            values.TryGetValue(match.Groups[1].Value, out var value) && !string.IsNullOrWhiteSpace(value)
                ? match.Groups[2].Value
                : string.Empty);
        return VariablePattern().Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            if (!values.TryGetValue(name, out var value))
                throw new InvalidOperationException($"Email template contains an unknown variable: {name}.");
            if (!htmlEncode) return value;
            var encoded = WebUtility.HtmlEncode(value);
            return name is "Description" or "Comment" or "CommentText" or "PreviousDescription" or "UpdatedDescription"
                ? encoded.Replace("\r\n", "<br>").Replace("\n", "<br>")
                : encoded;
        });
    }

    [GeneratedRegex(@"\{\{#([A-Za-z][A-Za-z0-9]*)\}\}([\s\S]*?)\{\{/\1\}\}")]
    private static partial Regex ConditionalPattern();

    [GeneratedRegex(@"\{\{([A-Za-z][A-Za-z0-9]*)\}\}")]
    private static partial Regex VariablePattern();
}
