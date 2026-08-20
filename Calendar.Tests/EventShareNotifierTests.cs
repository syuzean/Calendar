using Calendar.Services.Email;
using Xunit;

namespace Calendar.Tests;

public sealed class EventShareNotifierTests
{
    [Fact]
    public async Task NotificationEmail_ContainsEventAndOrganizerDetails()
    {
        var sender = new RecordingEmailSender();
        var notifier = new EventShareNotifier(sender);

        await notifier.NotifyAsync(new EventShareNotification(
            "collaborator@luma.test",
            "Planning session",
            new DateTime(2026, 10, 20, 9, 0, 0),
            new DateTime(2026, 10, 20, 10, 0, 0),
            false,
            "Owner"));

        var message = Assert.Single(sender.Messages);
        Assert.Equal("collaborator@luma.test", message.RecipientAddress);
        Assert.Contains("shared an event", message.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Planning session", message.Body);
        Assert.Contains("October 20, 2026", message.Body);
        Assert.Contains("9:00 AM", message.Body);
        Assert.Contains("Owner", message.Body);
    }

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
