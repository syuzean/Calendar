namespace Calendar.Models;

public sealed class EventParticipant
{
    public Guid EventId { get; set; }
    public CalendarEvent? Event { get; set; }
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
}
