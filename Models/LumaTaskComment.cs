namespace Calendar.Models;

public sealed class LumaTaskComment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public LumaTask? Task { get; set; }
    public AppUser? Author { get; set; }
}
