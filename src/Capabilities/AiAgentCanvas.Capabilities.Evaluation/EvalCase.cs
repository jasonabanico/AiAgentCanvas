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

    /// <summary>
    /// Tools a correct run is expected to call, comma separated. Supplying these
    /// turns the judge's opinion into a measurable tool-use accuracy.
    /// </summary>
    public string? ExpectedTools { get; set; }

    /// <summary>
    /// Tool calls a competent run needs. Trajectory efficiency is this over the
    /// count actually used, so an agent that reaches the right answer the long way
    /// scores below one.
    /// </summary>
    public int? OptimalSteps { get; set; }
}

public static class EvalCategories
{
    public const string ToolUse = "tool_use";
    public const string Planning = "planning";
    public const string Memory = "memory";
    public const string OutputQuality = "output_quality";
}
