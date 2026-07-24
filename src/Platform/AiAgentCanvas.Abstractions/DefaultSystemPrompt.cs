namespace AiAgentCanvas.Abstractions;

public sealed class DefaultSystemPrompt
{
    public string Value { get; }
    public DefaultSystemPrompt(string value) => Value = value;
}
