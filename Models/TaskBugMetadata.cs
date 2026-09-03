namespace Calendar.Models;

public enum WorkItemType
{
    Task = 0,
    Bug = 1
}

public enum BugCategory
{
    Functional = 0,
    Visual = 1,
    CrashError = 2,
    Performance = 3,
    ApiIntegration = 4,
    Data = 5,
    Regression = 6,
    Compatibility = 7
}

public enum BugSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public enum BugReproducibility
{
    Always = 0,
    Sometimes = 1,
    Once = 2,
    CannotReproduce = 3
}
