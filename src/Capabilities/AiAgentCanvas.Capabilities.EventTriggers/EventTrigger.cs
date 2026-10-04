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

    /// <summary>Runs this job instead of an agent. The message is passed as the <c>message</c> argument.</summary>
    public string? TargetJob { get; set; }

    /// <summary>For a connector trigger: the connection whose events fire it.</summary>
    public string? SourceConnection { get; set; }

    /// <summary>
    /// For a connector trigger: the event type to match, such as <c>sms.received</c>. A
    /// trailing <c>.*</c> matches a family (<c>sms.*</c>). Null matches every event.
    /// </summary>
    public string? SourceEventType { get; set; }

    public bool Enabled { get; set; } = true;
    public DateTimeOffset? LastFired { get; set; }
    public int FireCount { get; set; }
}

public enum EventTriggerType
{
    Scheduled,
    FileWatch,
    Webhook,

    /// <summary>Fired by an event from a connected service, such as a text message arriving.</summary>
    Connector,
}

public sealed class TriggerEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TriggerId { get; set; } = string.Empty;
    public string TriggerName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? TargetAgent { get; set; }
    public string? TargetJob { get; set; }
    public DateTimeOffset FiredAt { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>
    /// Identifies the occurrence, not the trigger. A second event with the same trigger
    /// and key is a duplicate and is dropped. Null means every event is distinct.
    /// </summary>
    public string? DedupeKey { get; set; }

    /// <summary>How many times dispatch has been attempted. Set by the queue.</summary>
    public int Attempts { get; set; }

    public string? LastError { get; set; }
}

public enum TriggerEventStatus
{
    Pending,
    Running,
    Succeeded,

    /// <summary>Failed every attempt. Held for an operator to inspect and retry.</summary>
    Dead,
}

public enum EnqueueOutcome
{
    Accepted,
    Duplicate,

    /// <summary>The backlog is at capacity. The caller keeps the event and retries later.</summary>
    Rejected,
}

public sealed record TriggerQueueCounts(int Pending, int Running, int Dead);
