namespace Calendar.Models;

public sealed class EventInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public CalendarEvent? Event { get; set; }
    public string RecipientEmail { get; set; } = string.Empty;
    public string NormalizedRecipientEmail { get; set; } = string.Empty;
    public Guid? ClaimedByUserId { get; set; }
    public AppUser? ClaimedByUser { get; set; }
    public EventInvitationStatus Status { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresUtc { get; set; }
    public DateTime? ClaimedUtc { get; set; }
    public string ResponseComment { get; set; } = string.Empty;
    public DateTime? ResponseUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public int EmailStatus { get; set; }
    public DateTime? EmailSentUtc { get; set; }
    public string? EmailLastError { get; set; }
}

public enum EventInvitationStatus { Pending = 0, Accepted = 1, Declined = 2, Revoked = 4 }
