namespace AiAgentCanvas.Host;

public sealed class FeatureFlags
{
    public const string SectionName = "Features";

    // Core agent identity
    public bool Personas { get; set; } = false;
    public bool Context { get; set; } = false;
    public bool Guardrails { get; set; } = false;
    public bool UserProfiles { get; set; } = false;
    public bool Entities { get; set; } = false;

    // Common capabilities
    public bool Skills { get; set; } = false;
    public bool SkillRegistry { get; set; } = false;
    public bool SkillAuthoring { get; set; } = false;
    public bool Workflows { get; set; } = false;
    public bool Mcp { get; set; } = false;

    // Operational
    public bool SystemTools { get; set; } = false;
    public bool Notifications { get; set; } = false;
    public bool Scheduling { get; set; } = false;
    public bool Rag { get; set; } = false;

    // Advanced / specialized
    public bool InterAgentCommunication { get; set; } = false;
    public bool EpisodicMemory { get; set; } = false;
    public bool AuditLog { get; set; } = false;
    public bool EventTriggers { get; set; } = false;
    public bool ComputerUse { get; set; } = false;
    public bool Evaluation { get; set; } = false;
}
