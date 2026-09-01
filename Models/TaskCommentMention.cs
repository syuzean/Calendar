namespace Calendar.Models;

public sealed class TaskCommentMention
{
    public Guid CommentId { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public LumaTaskComment? Comment { get; set; }
    public AppUser? User { get; set; }
}
