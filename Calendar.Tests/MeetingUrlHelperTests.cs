using Calendar.Services;
using Xunit;

namespace Calendar.Tests;

public sealed class MeetingUrlHelperTests
{
    [Theory]
    [InlineData("https://meet.google.com/abc-defg-hij", "Google Meet", "Join Google Meet")]
    [InlineData("https://company.zoom.us/j/123456789", "Zoom", "Join Zoom")]
    [InlineData("https://teams.microsoft.com/l/meetup-join/room", "Microsoft Teams", "Join Microsoft Teams")]
    [InlineData("https://video.example.test/room", "Online meeting", "Join meeting")]
    public void DetectsMeetingProvider(string url, string provider, string joinLabel)
    {
        Assert.Equal(provider, MeetingUrlHelper.ProviderName(url));
        Assert.Equal(joinLabel, MeetingUrlHelper.JoinLabel(url));
    }
}
