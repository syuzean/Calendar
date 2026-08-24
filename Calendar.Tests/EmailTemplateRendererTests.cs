using Calendar.Services.Email;
using Xunit;

namespace Calendar.Tests;

public sealed class EmailTemplateRendererTests
{
    [Fact]
    public void EventShared_RendersHtmlPlainTextAndEveryVariable()
    {
        var result = Renderer().RenderEventShared(Data());

        Assert.Contains("Owner shared", result.Subject);
        Assert.Contains("LUMA", result.HtmlBody);
        Assert.Contains("Calendar", result.HtmlBody);
        Assert.Contains("Open in LUMA", result.HtmlBody);
        Assert.Contains("Recipient", result.HtmlBody);
        Assert.Contains("recipient@luma.test", result.HtmlBody);
        Assert.Contains("Planning session", result.HtmlBody);
        Assert.Contains("Tuesday, October 20, 2026", result.HtmlBody);
        Assert.Contains("9:00 AM", result.HtmlBody);
        Assert.Contains("10:00 AM", result.HtmlBody);
        Assert.Contains("Bring the roadmap", result.HtmlBody);
        Assert.Contains("#7654ee", result.HtmlBody);
        Assert.Contains("https://luma.test/event", result.HtmlBody);
        Assert.Contains("Google Meet", result.HtmlBody);
        Assert.Contains("Join meeting", result.HtmlBody);
        Assert.Contains("https://meet.google.com/abc-defg-hij", result.HtmlBody);
        Assert.Contains("Join meeting: https://meet.google.com/abc-defg-hij", result.PlainTextBody);
        Assert.Contains("Open in LUMA: https://luma.test/event", result.PlainTextBody);
        Assert.DoesNotContain("{{", result.HtmlBody);
        Assert.DoesNotContain("{{", result.PlainTextBody);
    }

    [Fact]
    public void EventShared_HtmlEncodesUserProvidedContent()
    {
        var data = Data() with
        {
            EventTitle = "<script>alert('title')</script>",
            Description = "<img src=x onerror=alert(1)>"
        };

        var result = Renderer().RenderEventShared(data);

        Assert.DoesNotContain("<script>", result.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", result.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", result.HtmlBody);
        Assert.Contains("&lt;img", result.HtmlBody);
        Assert.Contains("<script>alert('title')</script>", result.PlainTextBody);
    }

    [Fact]
    public void EventShared_OmitsEmptyDescriptionBlock()
    {
        var result = Renderer().RenderEventShared(Data() with { Description = string.Empty });

        Assert.DoesNotContain("Description:", result.PlainTextBody);
        Assert.DoesNotContain("margin-top:17px", result.HtmlBody);
    }

    [Fact]
    public void EventShared_OmitsMeetingSectionWhenNoMeetingUrlExists()
    {
        var result = Renderer().RenderEventShared(Data() with { MeetingUrl = string.Empty });

        Assert.DoesNotContain("Join meeting", result.HtmlBody);
        Assert.DoesNotContain("Join meeting:", result.PlainTextBody);
    }

    [Fact]
    public void EventUpdated_RendersConsistentHtmlPlainTextAndChangedFields()
    {
        var result = Renderer().RenderEventUpdated(new EventUpdatedTemplateData(
            "Recipient",
            "recipient@luma.test",
            "Owner",
            "Updated planning session",
            new DateTime(2026, 10, 21, 15, 0, 0),
            new DateTime(2026, 10, 21, 16, 0, 0),
            false,
            "Review the revised roadmap.",
            "#7654ee",
            "https://luma.test/?event=123",
            "https://zoom.us/j/123456789",
            "Zoom",
            "Title, Date, Time"));

        Assert.Contains("Updated:", result.Subject);
        Assert.Contains("The event has been updated", result.HtmlBody);
        Assert.Contains("Updated planning session", result.HtmlBody);
        Assert.Contains("Title, Date, Time", result.HtmlBody);
        Assert.Contains("Open in LUMA", result.HtmlBody);
        Assert.Contains("Join meeting", result.HtmlBody);
        Assert.Contains("Changed: Title, Date, Time", result.PlainTextBody);
        Assert.DoesNotContain("{{", result.HtmlBody);
    }

    [Fact]
    public void EventCancelled_RendersRecognitionDetailsWithoutActiveLinks()
    {
        var result = Renderer().RenderEventCancelled(new EventCancelledTemplateData(
            "Recipient",
            "recipient@luma.test",
            "Owner",
            "Planning session",
            new DateTime(2026, 10, 20, 9, 0, 0),
            new DateTime(2026, 10, 20, 10, 0, 0),
            false,
            "#7654ee"));

        Assert.Contains("Cancelled:", result.Subject);
        Assert.Contains("no longer taking place", result.HtmlBody);
        Assert.Contains("Planning session", result.HtmlBody);
        Assert.Contains("Tuesday, October 20, 2026", result.HtmlBody);
        Assert.Contains("9:00 AM", result.PlainTextBody);
        Assert.DoesNotContain("Join meeting", result.HtmlBody);
        Assert.DoesNotContain("Open in LUMA", result.HtmlBody);
        Assert.DoesNotContain("href=", result.HtmlBody, StringComparison.OrdinalIgnoreCase);
    }

    private static FileEmailTemplateRenderer Renderer() =>
        new(Path.Combine(AppContext.BaseDirectory, "EmailTemplates"));

    private static EventSharedTemplateData Data() => new(
        "Recipient",
        "recipient@luma.test",
        "Owner",
        "Planning session",
        new DateTime(2026, 10, 20, 9, 0, 0),
        new DateTime(2026, 10, 20, 10, 0, 0),
        false,
        "Bring the roadmap",
        "#7654ee",
        "https://luma.test/event",
        "https://meet.google.com/abc-defg-hij",
        "Google Meet");
}
