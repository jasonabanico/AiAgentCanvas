namespace AiAgentCanvas.Capabilities.Scheduling;

public sealed class ScheduledTaskRecord
{
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string? CronExpression { get; set; }
    public bool IsRecurring { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class ScheduledTaskResult
{
    public string TaskId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string CompletedAt { get; set; } = string.Empty;
}
