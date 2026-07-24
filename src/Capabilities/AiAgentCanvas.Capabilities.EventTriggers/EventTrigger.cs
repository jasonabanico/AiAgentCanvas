namespace AiAgentCanvas.Capabilities.EventTriggers;

public sealed class EventTrigger
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public EventTriggerType Type { get; set; }
    public string? CronExpression { get; set; }
    public string? WatchPath { get; set; }
    public string? Condition { get; set; }
    public string AgentMessage { get; set; } = string.Empty;
    public string? TargetAgent { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? LastFired { get; set; }
    public int FireCount { get; set; }
}

public enum EventTriggerType
{
    Scheduled,
    FileWatch,
    Webhook,
}

public sealed class TriggerEvent
{
    public string TriggerId { get; set; } = string.Empty;
    public string TriggerName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? TargetAgent { get; set; }
    public DateTimeOffset FiredAt { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string> Metadata { get; set; } = [];
}
