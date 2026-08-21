namespace Calendar.Services.Email;

public interface IEventShareNotifier
{
    Task NotifyAsync(EventShareNotification notification, CancellationToken cancellationToken = default);
}

public sealed record EventShareNotification(
    string RecipientName,
    string RecipientEmail,
    string EventTitle,
    DateTime Start,
    DateTime End,
    bool IsAllDay,
    string OrganizerName,
    string Description,
    string EventColor,
    string EventUrl,
    string MeetingUrl);
