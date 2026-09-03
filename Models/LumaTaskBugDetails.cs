namespace Calendar.Models;

public sealed class LumaTaskBugDetails
{
    public Guid TaskId { get; set; }
    public string? ExpectedResult { get; set; }
    public string? ObservedResult { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorDetails { get; set; }
    public string? Logs { get; set; }
    public string? ReproductionMarkdown { get; set; }
    public string? ExpectedDuration { get; set; }
    public string? ActualDuration { get; set; }
    public int? Attempts { get; set; }
    public string? HttpMethod { get; set; }
    public string? Endpoint { get; set; }
    public int? StatusCode { get; set; }
    public string? ApiRequest { get; set; }
    public string? ApiResponse { get; set; }
    public string? CorrelationId { get; set; }
    public string? DataEntity { get; set; }
    public string? DataIdentifier { get; set; }
    public string? ExpectedValue { get; set; }
    public string? ActualValue { get; set; }
    public string? LastKnownGoodVersion { get; set; }
    public string? FirstBrokenVersion { get; set; }
    public string? WorksOn { get; set; }
    public string? FailsOn { get; set; }
    public LumaTask? Task { get; set; }
}
