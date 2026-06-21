namespace AiAgentCanvas.Abstractions;

public interface IToolDependencySeed
{
    string AgentName { get; }
    IReadOnlyList<string> RequiredTools { get; }
}

public sealed class ToolDependencySeed : IToolDependencySeed
{
    public string AgentName { get; }
    public IReadOnlyList<string> RequiredTools { get; }

    public ToolDependencySeed(string agentName, IReadOnlyList<string> requiredTools)
    {
        AgentName = agentName;
        RequiredTools = requiredTools;
    }
}
