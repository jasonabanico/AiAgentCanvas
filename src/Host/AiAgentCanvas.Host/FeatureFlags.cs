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
    public bool RunLedger { get; set; } = false;
    public bool Jobs { get; set; } = false;

    /// <summary>Encrypted account credentials, OAuth connect flow and token refresh.</summary>
    public bool Connections { get; set; } = false;

    /// <summary>Connectors to outside services. Requires <see cref="Connections"/>.</summary>
    public bool Connectors { get; set; } = false;

    /// <summary>Answers in a fixed JSON shape, checked against a schema and retried until it fits.</summary>
    public bool StructuredOutput { get; set; } = false;

    /// <summary>Image tools: describe an image, or extract typed data from one.</summary>
    public bool Vision { get; set; } = false;

    /// <summary>Group chat, handoff and Magentic runs with durable checkpoints. Needs <see cref="InterAgentCommunication"/>.</summary>
    public bool AgentOrchestration { get; set; } = false;

    /// <summary>Serves a chosen set of this host's tools over MCP.</summary>
    public bool McpServer { get; set; } = false;
}
