namespace Calendar.Models;

public sealed class BugReproductionStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public int Position { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? ObservedResult { get; set; }
    public bool IsPrimaryFailure { get; set; }
    public LumaTask? Task { get; set; }
    public ICollection<TaskAttachment> Attachments { get; set; } = [];
}
