namespace Calendar.Models;

public sealed class TaskInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public LumaTask? Task { get; set; }
    public Guid InviterId { get; set; }
    public AppUser? Inviter { get; set; }
    public string RecipientEmail { get; set; } = string.Empty;
    public string NormalizedRecipientEmail { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresUtc { get; set; }
    public TaskInvitationStatus Status { get; set; } = TaskInvitationStatus.Pending;
    public Guid? ClaimedByUserId { get; set; }
    public AppUser? ClaimedByUser { get; set; }
    public DateTime? ClaimedUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public enum TaskInvitationStatus
{
    Pending = 0,
    Claimed = 1,
    Revoked = 2
}
