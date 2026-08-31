namespace Calendar.Models;

public sealed class LumaTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid CreatorId { get; set; }
    public Guid? AssigneeId { get; set; }
    public Guid? ProjectId { get; set; }
    public DateOnly? Deadline { get; set; }
    public DateTime CreatedAt { get; set; }
    public TaskAssignmentStatus AssignmentStatus { get; set; } = TaskAssignmentStatus.Pending;
    public TaskWorkStatus WorkStatus { get; set; } = TaskWorkStatus.ToDo;
    public TaskPriority Priority { get; set; } = TaskPriority.None;
    public DateTime? AcceptedAt { get; set; }
    public DateOnly? RequestedDeadline { get; set; }
    public string? DeadlineChangeComment { get; set; }
    public DateTime? DeadlineChangeRequestedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public AppUser? Creator { get; set; }
    public AppUser? Assignee { get; set; }
    public LumaProject? Project { get; set; }
    public TaskInvitation? Invitation { get; set; }
    public ICollection<LumaTaskComment> Comments { get; set; } = [];
}
