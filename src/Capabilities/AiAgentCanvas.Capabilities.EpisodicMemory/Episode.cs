namespace AiAgentCanvas.Capabilities.EpisodicMemory;

public sealed class Episode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string AgentName { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Outcome { get; set; } = "unknown";
    public List<string> ToolsUsed { get; set; } = [];
    public int TurnCount { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Decays over time so stale episodes fall out of recall. Distinct from
    /// <see cref="Importance"/>, which is fixed at write time and never decays.
    /// </summary>
    public double RelevanceScore { get; set; } = 1.0;

    /// <summary>
    /// How much this episode was worth remembering, 0 to 1. Episodes below the
    /// store's write threshold are not saved at all: an episodic store that keeps
    /// every turn is a transcript, not a memory.
    /// </summary>
    public double Importance { get; set; } = 0.5;

    /// <summary>
    /// Embedding of goal plus summary, when an embedding model is configured.
    /// Recall ranks by cosine similarity against it and falls back to keyword
    /// matching when it is absent.
    /// </summary>
    public float[]? Embedding { get; set; }
}
