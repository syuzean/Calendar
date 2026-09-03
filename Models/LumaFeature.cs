namespace Calendar.Models;

public sealed class LumaFeature
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public LumaProject? Project { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public ICollection<TaskFeature> TaskFeatures { get; set; } = [];
}
