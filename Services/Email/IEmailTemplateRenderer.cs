namespace Calendar.Services.Email;

public interface IEmailTemplateRenderer
{
    RenderedEmailTemplate RenderEventShared(EventSharedTemplateData data);
    RenderedEmailTemplate RenderEventUpdated(EventUpdatedTemplateData data);
    RenderedEmailTemplate RenderEventCancelled(EventCancelledTemplateData data);
}

public sealed record RenderedEmailTemplate(string Subject, string HtmlBody, string PlainTextBody);

public sealed record EventSharedTemplateData(
    string RecipientName,
    string RecipientEmail,
    string OrganizerName,
    string EventTitle,
    DateTime Start,
    DateTime End,
    bool IsAllDay,
    string Description,
    string EventColor,
    string EventUrl,
    string MeetingUrl,
    string MeetingProvider);

public sealed record EventUpdatedTemplateData(
    string RecipientName,
    string RecipientEmail,
    string OrganizerName,
    string EventTitle,
    DateTime Start,
    DateTime End,
    bool IsAllDay,
    string Description,
    string EventColor,
    string EventUrl,
    string MeetingUrl,
    string MeetingProvider,
    string ChangedFields);

public sealed record EventCancelledTemplateData(
    string RecipientName,
    string RecipientEmail,
    string OrganizerName,
    string EventTitle,
    DateTime Start,
    DateTime End,
    bool IsAllDay,
    string EventColor);
