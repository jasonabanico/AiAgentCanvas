namespace AiAgentCanvas.Abstractions;

public interface IGoalSeed
{
    string Name { get; }
    string Description { get; }
    string Priority { get; }
    string AcceptanceCriteria { get; }
    string? AssignedAgent { get; }
    string Content { get; }
}

public sealed class GoalSeed : IGoalSeed
{
    public string Name { get; }
    public string Description { get; }
    public string Priority { get; }
    public string AcceptanceCriteria { get; }
    public string? AssignedAgent { get; }
    public string Content { get; }

    public GoalSeed(string name, string description, string priority, string acceptanceCriteria, string? assignedAgent, string content)
    {
        Name = name;
        Description = description;
        Priority = priority;
        AcceptanceCriteria = acceptanceCriteria;
        AssignedAgent = assignedAgent;
        Content = content;
    }
}
