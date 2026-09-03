namespace Calendar.Models;

public sealed class TaskFeature
{
    public Guid TaskId { get; set; }
    public Guid FeatureId { get; set; }
    public LumaTask? Task { get; set; }
    public LumaFeature? Feature { get; set; }
}
