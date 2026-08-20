using System.Globalization;

namespace Calendar.Services.Email;

public sealed class EventShareNotifier(IEmailSender emailSender) : IEventShareNotifier
{
    public Task NotifyAsync(EventShareNotification notification, CancellationToken cancellationToken = default)
    {
        var subject = $"{notification.OrganizerName} shared an event with you";
        var body = $"""
            {notification.OrganizerName} shared an event with you on LUMA Calendar.

            Event: {notification.EventTitle}
            When: {FormatWhen(notification)}

            This shared event is now visible in your LUMA Calendar.
            """;

        return emailSender.SendAsync(
            new EmailMessage(notification.RecipientEmail, subject, body),
            cancellationToken);
    }

    private static string FormatWhen(EventShareNotification notification)
    {
        if (notification.IsAllDay)
            return notification.Start.Date == notification.End.Date.AddDays(-1)
                ? notification.Start.ToString("MMMM d, yyyy (all day)", CultureInfo.InvariantCulture)
                : $"{Format(notification.Start, "MMMM d, yyyy")} – {Format(notification.End.AddDays(-1), "MMMM d, yyyy")} (all day)";

        return notification.Start.Date == notification.End.Date
            ? $"{Format(notification.Start, "MMMM d, yyyy, h:mm tt")} – {Format(notification.End, "h:mm tt")}"
            : $"{Format(notification.Start, "MMMM d, yyyy, h:mm tt")} – {Format(notification.End, "MMMM d, yyyy, h:mm tt")}";
    }

    private static string Format(DateTime value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture);
}
