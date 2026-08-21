using Calendar.Services.Email;
using Xunit;

namespace Calendar.Tests;

public sealed class EventShareNotifierTests
{
    [Fact]
    public async Task NotificationEmail_UsesRenderedHtmlAndPlainTextBodies()
    {
        var sender = new RecordingEmailSender();
        var notifier = new EventShareNotifier(sender, TemplateRenderer());

        await notifier.NotifyAsync(new EventShareNotification(
            "Collaborator",
            "collaborator@luma.test",
            "Planning session",
            new DateTime(2026, 10, 20, 9, 0, 0),
            new DateTime(2026, 10, 20, 10, 0, 0),
            false,
            "Owner",
            "Bring the roadmap.",
            "blue",
            "https://luma.test/?event=123",
            "https://zoom.us/j/123456789"));

        var message = Assert.Single(sender.Messages);
        Assert.Equal("collaborator@luma.test", message.RecipientAddress);
        Assert.Contains("Planning session", message.Subject);
        Assert.Contains("October 20, 2026", message.PlainTextBody);
        Assert.Contains("9:00 AM", message.PlainTextBody);
        Assert.Contains("Open in LUMA", message.HtmlBody);
        Assert.Contains("#3d8fea", message.HtmlBody);
        Assert.Contains("Zoom", message.HtmlBody);
        Assert.Contains("Join meeting", message.HtmlBody);
        Assert.Contains("https://zoom.us/j/123456789", message.PlainTextBody);
    }

    private static FileEmailTemplateRenderer TemplateRenderer() =>
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
