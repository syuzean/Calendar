using Calendar.Models;
using Calendar.Services;
using Xunit;

namespace Calendar.Tests;

public sealed class CalendarTimeRulesTests
{
    [Fact]
    public void PastDateRule_UsesCalendarDateRatherThanTimeOfDay()
    {
        var todayAtFive = new DateTime(2026, 8, 21, 17, 0, 0);

        Assert.True(CalendarTimeRules.IsPastDate(new DateTime(2026, 8, 20, 23, 30, 0), todayAtFive));
        Assert.False(CalendarTimeRules.IsPastDate(new DateTime(2026, 8, 21, 2, 0, 0), todayAtFive));
        Assert.False(CalendarTimeRules.IsPastDate(new DateTime(2026, 8, 22, 0, 0, 0), todayAtFive));
    }

    [Fact]
    public void LiveRule_UsesInclusiveStartAndExclusiveEnd()
    {
        var item = new CalendarEvent
        {
            Start = new DateTime(2026, 8, 21, 17, 0, 0),
            End = new DateTime(2026, 8, 21, 18, 0, 0)
        };

        Assert.False(CalendarTimeRules.IsLive(item, item.Start.AddTicks(-1)));
        Assert.True(CalendarTimeRules.IsLive(item, item.Start));
        Assert.True(CalendarTimeRules.IsLive(item, item.Start.AddMinutes(30)));
        Assert.False(CalendarTimeRules.IsLive(item, item.End));
    }

    [Fact]
    public void LiveRule_HighlightsEveryActiveOverlappingEventIndependently()
    {
        var now = new DateTime(2026, 8, 25, 14, 30, 0);
        var activeEvents = new[]
        {
            new CalendarEvent { Start = now.AddMinutes(-30), End = now.AddMinutes(30) },
            new CalendarEvent { Start = now.AddMinutes(-15), End = now.AddMinutes(45) }
        };

        Assert.All(activeEvents, item => Assert.True(CalendarTimeRules.IsLive(item, now)));
    }
}
