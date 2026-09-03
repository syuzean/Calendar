namespace Calendar.Models;

public enum TaskChangeType
{
    Created = 0,
    FieldChanged = 1,
    TaskTaken = 2,
    AssignmentAccepted = 3,
    DeadlineChangeRequested = 4,
    DeadlineChangeApproved = 5,
    DeadlineChangeDeclined = 6,
    FeatureAdded = 7,
    FeatureRemoved = 8
}

public sealed class TaskChangeLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid MutationId { get; set; }
    public TaskChangeType ChangeType { get; set; }
    public string? FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime CreatedAt { get; set; }
    public LumaTask? Task { get; set; }
    public AppUser? ActorUser { get; set; }
}
