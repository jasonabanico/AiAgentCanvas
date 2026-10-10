using System.Text.Json;
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Orchestration.Services;

public sealed class ToolOutputOptions
{
    public const string SectionName = "Agent:ToolOutput";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The most characters of one tool result the model is shown. A web page, a file or a query
    /// can return megabytes, and one such result would push the rest of the conversation out
    /// of the prompt budget. The cut is marked so the model knows it saw a part.
    /// </summary>
    public int MaxChars { get; set; } = 24_000;
}

/// <summary>Caps the size of what a tool returns, and tells the model when it cut something off.</summary>
public sealed class BoundedOutputAIFunction : DelegatingAIFunction
{
    private readonly ToolOutputOptions _options;

    public BoundedOutputAIFunction(AIFunction inner, ToolOutputOptions options) : base(inner)
    {
        _options = options;
    }

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var result = await base.InvokeCoreAsync(arguments, cancellationToken);
        if (!_options.Enabled || _options.MaxChars <= 0)
            return result;

        var text = result switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
            JsonElement e => e.GetRawText(),
            _ => null,
        };

        if (text is null || text.Length <= _options.MaxChars)
            return result;

        AgentTelemetry.ContextCompactions.Add(1, new KeyValuePair<string, object?>("strategy", "tool_output"));
        return Bound(text, _options.MaxChars);
    }

    internal static string Bound(string text, int max) =>
        text[..max] + $"\n[output cut: showing {max:N0} of {text.Length:N0} characters. Ask for a narrower result, or for the part you need.]";
}
