namespace AiAgentCanvas.Abstractions;

public interface IWorkflowSeed
{
    string Name { get; }
    string Description { get; }
    string? Tags { get; }
    string Content { get; }
}

public sealed class WorkflowSeed : IWorkflowSeed
{
    public string Name { get; }
    public string Description { get; }
    public string? Tags { get; }
    public string Content { get; }

    public WorkflowSeed(string name, string description, string? tags, string content)
    {
        Name = name;
        Description = description;
        Tags = tags;
        Content = content;
    }
}
