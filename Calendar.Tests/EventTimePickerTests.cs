using Calendar.Services;
using Xunit;

namespace Calendar.Tests;

public sealed class EventTimePickerTests
{
    [Fact]
    public void StartOptions_ContainOnlyCompleteHalfHourValues()
    {
        Assert.Equal(48, EventTimePicker.StartOptions.Count);
        Assert.Equal("00:00", EventTimePicker.StartOptions[0].Value);
        Assert.Equal("12:00 AM", EventTimePicker.StartOptions[0].Label);
        Assert.Equal("08:30", EventTimePicker.StartOptions[17].Value);
        Assert.Equal("08:30 AM", EventTimePicker.StartOptions[17].Label);
        Assert.All(EventTimePicker.StartOptions, option =>
            Assert.Contains(option.Value[^2..], new[] { "00", "30" }));
    }

    [Theory]
    [InlineData("08:00", "0|09:00")]
    [InlineData("08:30", "0|09:30")]
    [InlineData("23:30", "1|00:30")]
    public void ChangingStart_DefaultsEndToOneHourLater(string start, string expectedEnd)
    {
        Assert.Equal(expectedEnd, EventTimePicker.DefaultEndValue(start));
    }

    [Fact]
    public void OvernightEnd_IsExplicitlyLabeledAsNextDay()
    {
        var options = EventTimePicker.EndOptions("23:30");

        Assert.Equal("1|00:00", options[0].Value);
        Assert.Equal("12:00 AM · next day", options[0].Label);
    }

    [Theory]
    [InlineData("08:15", "0|09:00")]
    [InlineData("08:00", "0|08:00")]
    [InlineData("08:00", "0|08:15")]
    public void InvalidOrArbitraryTimes_AreRejected(string startValue, string endValue)
    {
        Assert.False(EventTimePicker.TryResolve(
            new DateTime(2026, 8, 24), startValue, endValue, out _, out _));
    }

    [Fact]
    public void ValidNextDaySelection_ResolvesWithoutSilentRollover()
    {
        var resolved = EventTimePicker.TryResolve(
            new DateTime(2026, 8, 24), "23:30", "1|00:30", out var start, out var end);

        Assert.True(resolved);
        Assert.Equal(new DateTime(2026, 8, 24, 23, 30, 0), start);
        Assert.Equal(new DateTime(2026, 8, 25, 0, 30, 0), end);
    }
}
