namespace AiAgentCanvas.Capabilities.Evaluation;

public sealed class EvalResult
{
    public long Id { get; set; }
    public long EvalCaseId { get; set; }
    public string EvalCaseName { get; set; } = string.Empty;
    public string ActualOutput { get; set; } = string.Empty;
    public double Score { get; set; }
    public bool Passed { get; set; }
    public string Rationale { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public DateTimeOffset RunAt { get; set; } = DateTimeOffset.UtcNow;
}
