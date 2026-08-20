using Calendar.Models;

namespace Calendar.Components.Calendar;

public sealed record EventContextRequest(CalendarEvent Event, double ClientX, double ClientY);
