using System.ComponentModel.DataAnnotations.Schema;

namespace Calendar.Models;

public sealed class CalendarEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool IsAllDay { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Color { get; set; } = "violet";
    public bool IsPublic { get; set; }
    public Guid OwnerId { get; set; }
    public AppUser? Owner { get; set; }
    public ICollection<EventParticipant> Participants { get; set; } = [];

    [NotMapped] public string OwnerName { get; set; } = string.Empty;
    [NotMapped] public bool CanEdit { get; set; }
    [NotMapped] public bool IsCollaborator { get; set; }
    [NotMapped] public List<string> CollaboratorEmails { get; set; } = [];

    public CalendarEvent Copy() => new()
    {
        Id = Id,
        Title = Title,
        Start = Start,
        End = End,
        IsAllDay = IsAllDay,
        Description = Description,
        Color = Color,
        IsPublic = IsPublic,
        OwnerId = OwnerId,
        OwnerName = OwnerName,
        CanEdit = CanEdit,
        IsCollaborator = IsCollaborator,
        CollaboratorEmails = [.. CollaboratorEmails]
    };
}
