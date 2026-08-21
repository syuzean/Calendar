using Calendar.Services;

namespace Calendar.Services.Email;

public sealed class EventShareNotifier(
    IEmailSender emailSender,
    IEmailTemplateRenderer templateRenderer) : IEventShareNotifier
{
    public Task NotifyAsync(EventShareNotification notification, CancellationToken cancellationToken = default)
    {
        var rendered = templateRenderer.RenderEventShared(new EventSharedTemplateData(
            notification.RecipientName,
            notification.RecipientEmail,
            notification.OrganizerName,
            notification.EventTitle,
            notification.Start,
            notification.End,
            notification.IsAllDay,
            notification.Description,
            EventColor(notification.EventColor),
            notification.EventUrl,
            notification.MeetingUrl,
            MeetingUrlHelper.ProviderName(notification.MeetingUrl)));

        return emailSender.SendAsync(
            new EmailMessage(
                notification.RecipientEmail,
                rendered.Subject,
                rendered.PlainTextBody,
                rendered.HtmlBody),
            cancellationToken);
    }

    private static string EventColor(string color) => color.ToLowerInvariant() switch
    {
        "blue" => "#3d8fea",
        "green" => "#38a878",
        "orange" => "#ea8b3f",
        "rose" => "#df5e89",
        _ => "#7654ee"
    };
}
