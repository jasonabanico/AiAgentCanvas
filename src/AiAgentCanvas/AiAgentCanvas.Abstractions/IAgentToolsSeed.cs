namespace AiAgentCanvas.Abstractions;

public interface IAgentToolsSeed
{
    string AgentName { get; }
    IReadOnlyList<string> ToolNames { get; }
}

public sealed class AgentToolsSeed : IAgentToolsSeed
{
    public string AgentName { get; }
    public IReadOnlyList<string> ToolNames { get; }

    public AgentToolsSeed(string agentName, IReadOnlyList<string> toolNames)
    {
        AgentName = agentName;
        ToolNames = toolNames;
    }
}
