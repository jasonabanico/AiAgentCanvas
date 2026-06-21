namespace AiAgentCanvas.Abstractions;

public interface IEntitySeed
{
    string Name { get; }
    string Type { get; }
    string? Tags { get; }
    string Content { get; }
}

public sealed class EntitySeed : IEntitySeed
{
    public string Name { get; }
    public string Type { get; }
    public string? Tags { get; }
    public string Content { get; }

    public EntitySeed(string name, string type, string? tags, string content)
    {
        Name = name;
        Type = type;
        Tags = tags;
        Content = content;
    }
}
