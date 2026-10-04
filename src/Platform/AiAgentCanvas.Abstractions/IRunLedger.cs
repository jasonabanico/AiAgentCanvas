namespace AiAgentCanvas.Abstractions;

public enum RunStatus
{
    Running,
    Succeeded,
    Failed,
    Cancelled,

    /// <summary>The process stopped while the run was in flight, so its outcome is unknown.</summary>
    Abandoned,
}

public sealed class RunRecord
{
    public string Id { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public RunSource Source { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string? TriggerId { get; set; }
    public string? TaskId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public RunStatus Status { get; set; }
    public string? Input { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public int ModelCalls { get; set; }
    public double EstimatedCost { get; set; }
    public int ToolCallCount { get; set; }
    public string? Termination { get; set; }
    public IReadOnlyList<ToolCallRecord> ToolCalls { get; set; } = [];
}

public sealed record RunQuery(
    int Limit = 20,
    DateTimeOffset? Since = null,
    string? AgentName = null,
    RunStatus? Status = null,
    RunSource? Source = null,
    string? TriggerId = null,
    bool TopLevelOnly = false);

/// <summary>Totals over top-level runs only, so delegated work is not counted twice.</summary>
public sealed record RunTotals(int Runs, int Failed, long InputTokens, long OutputTokens, double EstimatedCost);

/// <summary>
/// One record per run: what started it, what it did, what it cost, how it ended.
/// The audit log records events; this answers "what did it do last night".
/// </summary>
public interface IRunLedger
{
    void Start(RunRecord run);

    void Finish(string runId, RunStatus status, string? output, string? error, RunUsage usage, DateTimeOffset endedAt);

    IReadOnlyList<RunRecord> Recent(RunQuery query);

    RunRecord? Get(string runId);

    /// <summary>Runs started inside the given run, oldest first.</summary>
    IReadOnlyList<RunRecord> Children(string parentId);

    RunTotals Totals(DateTimeOffset since, string? agentName = null, string? triggerId = null);

    /// <summary>Deletes finished runs that ended before the cutoff. Returns the number removed.</summary>
    int Prune(DateTimeOffset olderThan);

    /// <summary>Marks runs left in the running state by a previous process. Returns the number marked.</summary>
    int MarkAbandoned();
}
