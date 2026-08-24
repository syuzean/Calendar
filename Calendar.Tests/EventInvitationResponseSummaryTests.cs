using Calendar.Models;
using Xunit;

namespace Calendar.Tests;

public sealed class EventInvitationResponseSummaryTests
{
    [Fact]
    public void Summary_CountsAcceptedDeclinedAndPending()
    {
        var responses = new[]
        {
            Response(EventInvitationStatus.Accepted),
            Response(EventInvitationStatus.Accepted),
            Response(EventInvitationStatus.Declined),
            Response(EventInvitationStatus.Pending),
            Response(EventInvitationStatus.Pending),
            Response(EventInvitationStatus.Pending)
        };

        var summary = EventInvitationResponseSummary.From(responses);

        Assert.Equal(2, summary.Accepted);
        Assert.Equal(1, summary.Declined);
        Assert.Equal(3, summary.Pending);
    }

    [Fact]
    public void Summary_UpdatesAfterResponseChanges()
    {
        var responses = new[]
        {
            Response(EventInvitationStatus.Accepted),
            Response(EventInvitationStatus.Pending)
        };
        var before = EventInvitationResponseSummary.From(responses);

        responses[1] = responses[1] with { Status = EventInvitationStatus.Declined };
        var after = EventInvitationResponseSummary.From(responses);

        Assert.Equal((1, 0, 1), (before.Accepted, before.Declined, before.Pending));
        Assert.Equal((1, 1, 0), (after.Accepted, after.Declined, after.Pending));
    }

    private static EventInvitationResponseView Response(EventInvitationStatus status) =>
        new(Guid.NewGuid(), "Recipient", "recipient@luma.test", status, string.Empty, null, false);
}
