namespace AiAgentCanvas.Host;

public sealed class FeatureFlags
{
    public const string SectionName = "Features";

    public bool SystemTools { get; set; } = true;
    public bool Notifications { get; set; } = true;
    public bool Scheduling { get; set; } = true;
    public bool Skills { get; set; } = true;
    public bool Mcp { get; set; } = true;
    public bool Personas { get; set; } = true;
    public bool Context { get; set; } = true;
    public bool Workflows { get; set; } = true;
    public bool Entities { get; set; } = true;
    public bool UserProfiles { get; set; } = true;
    public bool Guardrails { get; set; } = true;
    public bool SkillRegistry { get; set; } = true;
    public bool SkillAuthoring { get; set; } = true;
    public bool EpisodicMemory { get; set; } = true;
    public bool AuditLog { get; set; } = true;
    public bool EventTriggers { get; set; } = true;
    public bool ComputerUse { get; set; } = true;
    public bool InterAgentCommunication { get; set; } = true;
    public bool Rag { get; set; } = true;
    public bool MarketData { get; set; } = true;
    public bool FinancialAnalyst { get; set; } = true;
}
