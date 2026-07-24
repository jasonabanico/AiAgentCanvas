namespace AiAgentCanvas.Abstractions;

public interface IContextSeed
{
    string Topic { get; }
    string? Type { get; }
    string? Tags { get; }
    string Content { get; }
}

public sealed class ContextSeed : IContextSeed
{
    public string Topic { get; }
    public string? Type { get; }
    public string? Tags { get; }
    public string Content { get; }

    public ContextSeed(string topic, string? tags, string content, string? type = null)
    {
        Topic = topic;
        Type = type;
        Tags = tags;
        Content = content;
    }
}
