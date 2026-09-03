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
    public WorkItemType WorkItemType { get; set; } = WorkItemType.Task;
    public BugCategory? BugCategory { get; set; }
    public BugSeverity? BugSeverity { get; set; }
    public BugReproducibility? BugReproducibility { get; set; }
    public string? FoundInVersion { get; set; }
    public string? BugEnvironment { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateOnly? RequestedDeadline { get; set; }
    public string? DeadlineChangeComment { get; set; }
    public DateTime? DeadlineChangeRequestedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public AppUser? Creator { get; set; }
    public AppUser? Assignee { get; set; }
    public LumaProject? Project { get; set; }
    public TaskInvitation? Invitation { get; set; }
    public LumaTaskBugDetails? BugDetails { get; set; }
    public ICollection<BugReproductionStep> ReproductionSteps { get; set; } = [];
    public ICollection<LumaTaskComment> Comments { get; set; } = [];
    public ICollection<InboxItem> InboxItems { get; set; } = [];
    public ICollection<TaskAttachment> Attachments { get; set; } = [];
    public ICollection<TaskMention> Mentions { get; set; } = [];
}
