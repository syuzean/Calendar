namespace Calendar.Models;

public sealed class InboxItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RecipientUserId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid? TaskId { get; set; }
    public InboxActivityType ActivityType { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public AppUser? Recipient { get; set; }
    public AppUser? Actor { get; set; }
    public LumaTask? Task { get; set; }
}

public enum InboxActivityType
{
    TaskAssigned,
    TaskTaken,
    TaskAccepted,
    DeadlineChangeRequested,
    DeadlineChangeApproved,
    DeadlineChangeDeclined,
    WorkStatusChanged,
    CommentAdded,
    TaskUpdated,
    TaskMentioned
}
