namespace Calendar.Models;

public sealed class LumaProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public AppUser? CreatedByUser { get; set; }
    public ICollection<LumaTask> Tasks { get; set; } = [];
    public ICollection<LumaFeature> Features { get; set; } = [];
}
