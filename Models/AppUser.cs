namespace Calendar.Models;

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public ICollection<CalendarEvent> OwnedEvents { get; set; } = [];
    public ICollection<EventParticipant> Participations { get; set; } = [];
    public ICollection<EventInvitation> ClaimedInvitations { get; set; } = [];
    public ICollection<LumaTask> CreatedTasks { get; set; } = [];
    public ICollection<LumaTask> AssignedTasks { get; set; } = [];
    public ICollection<LumaTaskComment> TaskComments { get; set; } = [];
    public ICollection<TaskInvitation> SentTaskInvitations { get; set; } = [];
    public ICollection<TaskInvitation> ClaimedTaskInvitations { get; set; } = [];
    public ICollection<LumaProject> CreatedProjects { get; set; } = [];
    public ICollection<InboxItem> ReceivedInboxItems { get; set; } = [];
    public ICollection<InboxItem> AuthoredInboxItems { get; set; } = [];
    public ICollection<TaskAttachment> UploadedTaskAttachments { get; set; } = [];
    public ICollection<TaskMention> TaskMentions { get; set; } = [];
    public ICollection<TaskCommentMention> TaskCommentMentions { get; set; } = [];
    public ICollection<TaskChangeLog> AuthoredTaskChanges { get; set; } = [];
    public ICollection<LumaFeature> CreatedFeatures { get; set; } = [];
}
