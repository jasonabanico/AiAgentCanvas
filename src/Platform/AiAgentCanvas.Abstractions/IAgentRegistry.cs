namespace AiAgentCanvas.Abstractions;

public sealed class AgentPersonaInfo
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Instructions { get; init; }
}

public interface IAgentRegistry
{
    IReadOnlyList<string> ListAvailableAgents();
    AgentPersonaInfo? GetAgentInfo(string name);
    void Invalidate(string name);
}
