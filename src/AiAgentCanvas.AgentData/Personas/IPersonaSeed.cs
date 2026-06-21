namespace AiAgentCanvas.AgentData.Personas;

public interface IPersonaSeed
{
    string Name { get; }
    string Description { get; }
    string Instructions { get; }
}

public sealed class PersonaSeed : IPersonaSeed
{
    public string Name { get; }
    public string Description { get; }
    public string Instructions { get; }

    public PersonaSeed(string name, string description, string instructions)
    {
        Name = name;
        Description = description;
        Instructions = instructions;
    }
}
