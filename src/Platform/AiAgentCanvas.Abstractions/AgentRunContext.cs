namespace AiAgentCanvas.Abstractions;

/// <summary>Where a run came from. Interactive chat is not tracked, because the user is watching it.</summary>
public enum RunSource
{
    Interactive,
    Scheduled,
    Trigger,
    Handoff,
    Job,

    /// <summary>A group chat, handoff or Magentic run of several agents.</summary>
    Orchestration,

    /// <summary>A tool called by an outside client, such as another agent over MCP.</summary>
    External,
}

public sealed record ToolCallRecord(string Name, string Outcome, double DurationMs);

/// <summary>What a run consumed, read from <see cref="AgentRunContext.Snapshot"/>.</summary>
public sealed record RunUsage(
    long InputTokens,
    long OutputTokens,
    int ModelCalls,
    double EstimatedCost,
    int ToolCallCount,
    IReadOnlyList<ToolCallRecord> ToolCalls,
    string? Termination);

/// <summary>
/// The run executing on the current async flow. The model client, the tool wrapper and
/// the loop guard each see only their own call, so none of them can say what a whole
/// run cost. They each write to the ambient context instead, and whoever started the
/// run reads the total when it ends.
/// </summary>
/// <remarks>
/// A run started inside another run is its child. Usage rolls up to the parent, so a
/// parent's totals include the work it delegated.
/// </remarks>
public sealed class AgentRunContext
{
    /// <summary>Upper bound on stored tool call detail. The count keeps going past it.</summary>
    public const int MaxRecordedToolCalls = 200;

    private static readonly AsyncLocal<AgentRunContext?> Ambient = new();

    private readonly object _gate = new();
    private readonly List<ToolCallRecord> _toolCalls = [];
    private long _inputTokens;
    private long _outputTokens;
    private int _modelCalls;
    private double _cost;
    private int _toolCallCount;
    private string? _termination;

    public AgentRunContext(
        string runId,
        RunSource source,
        string agentName,
        AgentRunContext? parent = null,
        string? triggerId = null,
        string? taskId = null,
        DateTimeOffset? startedAt = null)
    {
        RunId = runId;
        Source = source;
        AgentName = agentName;
        Parent = parent;
        TriggerId = triggerId;
        TaskId = taskId;
        StartedAt = startedAt ?? DateTimeOffset.UtcNow;
    }

    /// <summary>The run on this async flow, or null when nothing is tracking one.</summary>
    public static AgentRunContext? Current => Ambient.Value;

    public string RunId { get; }
    public RunSource Source { get; }
    public string AgentName { get; }
    public AgentRunContext? Parent { get; }
    public string? TriggerId { get; }
    public string? TaskId { get; }
    public DateTimeOffset StartedAt { get; }

    /// <summary>Spend so far in the configured currency. Read by the per-run cost limit.</summary>
    public double EstimatedCost
    {
        get { lock (_gate) return _cost; }
    }

    /// <summary>Makes this the ambient run until the returned scope is disposed.</summary>
    public IDisposable Enter()
    {
        var previous = Ambient.Value;
        Ambient.Value = this;
        return new Restore(previous);
    }

    public void AddModelUsage(long inputTokens, long outputTokens, double cost)
    {
        lock (_gate)
        {
            _modelCalls++;
            _inputTokens += inputTokens;
            _outputTokens += outputTokens;
            _cost += cost;
        }

        Parent?.RollUpModelUsage(inputTokens, outputTokens, cost);
    }

    public void AddToolCall(string name, string outcome, double durationMs)
    {
        lock (_gate)
        {
            _toolCallCount++;
            if (_toolCalls.Count < MaxRecordedToolCalls)
                _toolCalls.Add(new ToolCallRecord(name, outcome, durationMs));
        }

        Parent?.RollUpToolCall();
    }

    /// <summary>Records why the loop guard cut the run short. The first reason is kept.</summary>
    public void SetTermination(string reason)
    {
        lock (_gate)
            _termination ??= reason;
    }

    public RunUsage Snapshot()
    {
        lock (_gate)
        {
            return new RunUsage(
                _inputTokens,
                _outputTokens,
                _modelCalls,
                _cost,
                _toolCallCount,
                _toolCalls.ToList(),
                _termination);
        }
    }

    private void RollUpModelUsage(long inputTokens, long outputTokens, double cost)
    {
        lock (_gate)
        {
            _modelCalls++;
            _inputTokens += inputTokens;
            _outputTokens += outputTokens;
            _cost += cost;
        }

        Parent?.RollUpModelUsage(inputTokens, outputTokens, cost);
    }

    private void RollUpToolCall()
    {
        lock (_gate)
            _toolCallCount++;

        Parent?.RollUpToolCall();
    }

    private sealed class Restore(AgentRunContext? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
