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
    public double RelevanceScore { get; set; } = 1.0;
}
