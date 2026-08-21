using Calendar.Models;

namespace Calendar.Services;

public static class CalendarTimeRules
{
    public const string PastDateMessage = "Events cannot be created on past dates.";

    public static bool IsPastDate(DateTime value, DateTime today) => value.Date < today.Date;

    public static bool IsLive(CalendarEvent item, DateTime currentMoment) =>
        item.Start <= currentMoment && currentMoment < item.End;
}
