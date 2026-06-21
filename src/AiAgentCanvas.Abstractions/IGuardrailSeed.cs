namespace AiAgentCanvas.Abstractions;

public interface IGuardrailSeed
{
    string Name { get; }
    string Severity { get; }
    bool Enabled { get; }
    string Rule { get; }
}

public sealed class GuardrailSeed : IGuardrailSeed
{
    public string Name { get; }
    public string Severity { get; }
    public bool Enabled { get; }
    public string Rule { get; }

    public GuardrailSeed(string name, string severity, bool enabled, string rule)
    {
        Name = name;
        Severity = severity;
        Enabled = enabled;
        Rule = rule;
    }
}
