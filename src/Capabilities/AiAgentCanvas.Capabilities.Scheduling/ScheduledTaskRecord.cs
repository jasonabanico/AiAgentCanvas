namespace AiAgentCanvas.Capabilities.Scheduling;

public sealed class ScheduledTaskRecord
{
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string? CronExpression { get; set; }
    public bool IsRecurring { get; set; }
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>
    /// When the runner last executed this task. Null means it has never run, which
    /// is what makes a one-shot task due and gives a recurring task its first tick.
    /// </summary>
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>Set when the last execution threw, so a failing task is visible in the list.</summary>
    public string? LastError { get; set; }
}

public sealed class ScheduledTaskResult
{
    public string TaskId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string CompletedAt { get; set; } = string.Empty;
}
