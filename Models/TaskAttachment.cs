namespace Calendar.Models;

public sealed class TaskAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public Guid? BugReproductionStepId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public LumaTask? Task { get; set; }
    public AppUser? UploadedByUser { get; set; }
    public BugReproductionStep? BugReproductionStep { get; set; }
}
