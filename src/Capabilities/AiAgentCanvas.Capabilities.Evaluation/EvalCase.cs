namespace AiAgentCanvas.Capabilities.Evaluation;

public sealed class EvalCase
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = EvalCategories.OutputQuality;
    public string Input { get; set; } = string.Empty;
    public string ExpectedCriteria { get; set; } = string.Empty;
    public string? Tags { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class EvalCategories
{
    public const string ToolUse = "tool_use";
    public const string Planning = "planning";
    public const string Memory = "memory";
    public const string OutputQuality = "output_quality";
}
