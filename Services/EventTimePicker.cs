using System.Globalization;

namespace Calendar.Services;

public static class EventTimePicker
{
    private const string TimeFormat = "HH:mm";
    private static readonly DateTime TimeAnchor = new(2000, 1, 1);

    public static IReadOnlyList<TimePickerOption> StartOptions { get; } = Enumerable.Range(0, 48)
        .Select(index => TimeAnchor.AddMinutes(index * 30))
        .Select(value => new TimePickerOption(
            value.ToString(TimeFormat, CultureInfo.InvariantCulture),
            value.ToString("hh:mm tt", CultureInfo.InvariantCulture)))
        .ToList();

    public static IReadOnlyList<TimePickerOption> EndOptions(string startValue, string? selectedValue = null)
    {
        if (!TryParseTime(startValue, out var startTime)) return [];

        var start = TimeAnchor.Add(startTime);
        var options = Enumerable.Range(1, 48)
            .Select(step => start.AddMinutes(step * 30))
            .Select(value =>
            {
                var dayOffset = (value.Date - TimeAnchor.Date).Days;
                return new TimePickerOption(ToEndValue(dayOffset, value.TimeOfDay), EndLabel(value, dayOffset));
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(selectedValue) &&
            !options.Any(option => option.Value == selectedValue) &&
            TryParseEnd(selectedValue, out var selectedDayOffset, out var selectedTime))
        {
            var selected = TimeAnchor.AddDays(selectedDayOffset).Add(selectedTime);
            if (selected > start)
                options.Insert(0, new TimePickerOption(selectedValue, EndLabel(selected, selectedDayOffset)));
        }

        return options;
    }

    public static string StartValue(DateTime value) => value.ToString(TimeFormat, CultureInfo.InvariantCulture);

    public static string EndValue(DateTime start, DateTime end) =>
        ToEndValue(Math.Max(0, (end.Date - start.Date).Days), end.TimeOfDay);

    public static string DefaultEndValue(string startValue)
    {
        if (!TryParseTime(startValue, out var startTime)) return string.Empty;
        var end = TimeAnchor.Add(startTime).AddHours(1);
        return ToEndValue((end.Date - TimeAnchor.Date).Days, end.TimeOfDay);
    }

    public static bool TryResolve(
        DateTime date,
        string startValue,
        string endValue,
        out DateTime start,
        out DateTime end)
    {
        start = default;
        end = default;
        if (!TryParseTime(startValue, out var startTime) ||
            !TryParseEnd(endValue, out var endDayOffset, out var endTime)) return false;

        start = date.Date.Add(startTime);
        end = date.Date.AddDays(endDayOffset).Add(endTime);
        return end > start && end - start >= TimeSpan.FromMinutes(30) &&
            IsHalfHourBoundary(start) && IsHalfHourBoundary(end);
    }

    private static string EndLabel(DateTime value, int dayOffset)
    {
        var suffix = dayOffset switch
        {
            0 => string.Empty,
            1 => " · next day",
            _ => $" · {dayOffset} days later"
        };
        return value.ToString("hh:mm tt", CultureInfo.InvariantCulture) + suffix;
    }

    private static string ToEndValue(int dayOffset, TimeSpan time) =>
        $"{dayOffset}|{TimeAnchor.Add(time):HH:mm}";

    private static bool TryParseTime(string value, out TimeSpan time) =>
        TimeSpan.TryParseExact(value, "hh\\:mm", CultureInfo.InvariantCulture, out time) &&
        time >= TimeSpan.Zero && time < TimeSpan.FromDays(1) && time.Minutes % 30 == 0;

    private static bool TryParseEnd(string value, out int dayOffset, out TimeSpan time)
    {
        dayOffset = 0;
        time = default;
        var parts = value.Split('|', 2);
        return parts.Length == 2 && int.TryParse(parts[0], out dayOffset) && dayOffset is >= 0 and <= 366 &&
            TryParseTime(parts[1], out time);
    }

    private static bool IsHalfHourBoundary(DateTime value) =>
        value.Minute % 30 == 0 && value.Ticks % TimeSpan.TicksPerMinute == 0;
}

public sealed record TimePickerOption(string Value, string Label);
