namespace AiAgentCanvas.Capabilities.Evaluation;

public sealed class EvalResult
{
    public long Id { get; set; }
    public long EvalCaseId { get; set; }
    public string EvalCaseName { get; set; } = string.Empty;
    public string ActualOutput { get; set; } = string.Empty;

    /// <summary>Judge score for the response, 0 to 1.</summary>
    public double Score { get; set; }

    public bool Passed { get; set; }
    public string Rationale { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public DateTimeOffset RunAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Tool calls the run made. Zero on a case the agent answered from the model alone.</summary>
    public int ToolCalls { get; set; }

    /// <summary>Distinct tools the run used, comma separated.</summary>
    public string ToolsUsed { get; set; } = string.Empty;

    /// <summary>
    /// Fraction of expected tools the run actually called. Null when the case did
    /// not declare expected tools.
    /// </summary>
    public double? ToolUseAccuracy { get; set; }

    /// <summary>
    /// Optimal tool calls over actual, capped at 1. Null when the case did not
    /// declare an optimal step count, and 0 for a run that failed.
    /// </summary>
    public double? TrajectoryEfficiency { get; set; }

    /// <summary>True when the run was graded by a model other than the one under test.</summary>
    public bool IndependentJudge { get; set; }
}
