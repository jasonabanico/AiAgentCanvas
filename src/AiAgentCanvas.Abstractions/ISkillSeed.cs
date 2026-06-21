namespace AiAgentCanvas.Abstractions;

public interface ISkillSeed
{
    string Name { get; }
    string Description { get; }
    string PromptTemplate { get; }
}

public sealed class SkillSeed : ISkillSeed
{
    public string Name { get; }
    public string Description { get; }
    public string PromptTemplate { get; }

    public SkillSeed(string name, string description, string promptTemplate)
    {
        Name = name;
        Description = description;
        PromptTemplate = promptTemplate;
    }
}
