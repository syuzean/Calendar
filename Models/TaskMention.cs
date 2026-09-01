namespace Calendar.Models;

public sealed class TaskMention
{
    public Guid TaskId { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public LumaTask? Task { get; set; }
    public AppUser? User { get; set; }
}
