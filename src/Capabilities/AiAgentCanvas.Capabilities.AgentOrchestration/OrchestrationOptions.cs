namespace AiAgentCanvas.Capabilities.AgentOrchestration;

public sealed class OrchestrationOptions
{
    public const string SectionName = "Agent:Orchestration";

    public string DatabasePath { get; set; } = "orchestrations.db";

    /// <summary>Where workflow checkpoints are written, one folder per run.</summary>
    public string CheckpointDirectory { get; set; } = "orchestration-checkpoints";

    public int DefaultMaxRounds { get; set; } = 8;

    /// <summary>The most rounds any request may ask for.</summary>
    public int MaxRoundsCap { get; set; } = 30;

    public int MaxAgents { get; set; } = 8;

    public int RunTimeoutMinutes { get; set; } = 15;

    /// <summary>Finished runs, and their checkpoints, are deleted after this many days.</summary>
    public int RetentionDays { get; set; } = 30;
}
