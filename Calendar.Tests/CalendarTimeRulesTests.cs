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

        Assert.True(CalendarTimeRules.IsLive(item, item.Start));
        Assert.True(CalendarTimeRules.IsLive(item, item.Start.AddMinutes(30)));
        Assert.False(CalendarTimeRules.IsLive(item, item.End));
    }
}
