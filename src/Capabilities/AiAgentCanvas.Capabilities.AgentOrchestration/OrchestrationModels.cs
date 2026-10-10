using System.Text.Json;

namespace AiAgentCanvas.Capabilities.AgentOrchestration;

public enum OrchestrationKind
{
    /// <summary>Agents take turns on one shared conversation until the round limit.</summary>
    GroupChat,

    /// <summary>A lead agent passes the conversation to a specialist and takes it back.</summary>
    Handoff,

    /// <summary>A manager agent plans, assigns work, tracks progress and replans when stuck.</summary>
    Magentic,

    /// <summary>A pipeline. Each agent receives the conversation so far and adds its step, in the order named.</summary>
    Sequential,

    /// <summary>Every agent works on the same task at once and the answers are gathered. Use it for independent parts or for voting.</summary>
    Concurrent,

    /// <summary>
    /// Maker and checker. The first agent drafts, the second reviews the draft against the task
    /// and either approves it or says what to change. The loop ends on approval, on the round
    /// limit, or when a revision comes back unchanged.
    /// </summary>
    Review,
}

public enum OrchestrationStatus
{
    Running,

    /// <summary>Stopped at a checkpoint until a person answers. Nothing is running.</summary>
    WaitingForInput,

    Completed,
    Failed,
    Cancelled,

    /// <summary>The process stopped mid-run. The last checkpoint is intact, so it can be resumed.</summary>
    Interrupted,
}

/// <summary>What to run. <see cref="Agents"/> are names the agent registry knows.</summary>
/// <param name="Lead">The first agent for a handoff, or the manager for Magentic. Defaults to the default agent.</param>
/// <param name="MaxRounds">Turns in a group chat, coordination rounds in a Magentic run, or drafts in a Review run.</param>
/// <param name="RequireSignoff">Magentic only: stop for a person to approve the plan before any work starts.</param>
public sealed record OrchestrationSpec(
    OrchestrationKind Kind,
    string Task,
    IReadOnlyList<string> Agents,
    string? Lead = null,
    int? MaxRounds = null,
    bool RequireSignoff = true);

public sealed record TranscriptEntry(string Agent, string Text);

/// <summary>What a stopped run is waiting for a person to decide.</summary>
public sealed record PendingInput(string RequestId, string Kind, string Summary, JsonElement? Data);

public sealed class OrchestrationRun
{
    /// <summary>Also the workflow session id and the checkpoint folder name.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public OrchestrationSpec Spec { get; set; } = null!;
    public OrchestrationStatus Status { get; set; }
    public PendingInput? Pending { get; set; }
    public string? Result { get; set; }

    /// <summary>
    /// Why a Review run stopped: <see cref="ReviewTermination.Approved"/> is the only success.
    /// Null for the other kinds.
    /// </summary>
    public string? Termination { get; set; }

    public List<TranscriptEntry> Transcript { get; set; } = [];
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
