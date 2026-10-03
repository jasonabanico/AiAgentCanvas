using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Connectors;

/// <summary>
/// Conventions for the tools a connector exposes: a prefixed name, a risk, and results
/// that are structured text and not exceptions.
/// </summary>
public static partial class ConnectorTools
{
    public const string ConnectorKey = "connector.id";
    public const string RiskKey = "connector.risk";

    /// <summary>Creates a tool named <c>{ToolPrefix}_{verb}</c> tagged with its connector and risk.</summary>
    public static AIFunction Create(IConnectionContext context, string verb, string description, ToolRisk risk, Delegate method)
    {
        var name = $"{context.ToolPrefix}_{verb}";
        if (!ValidName().IsMatch(name))
        {
            throw new ArgumentException(
                $"Tool name '{name}' is not valid. Use lowercase letters, digits and underscores, starting with a letter, up to 64 characters.");
        }

        return AIFunctionFactory.Create(method, new AIFunctionFactoryOptions
        {
            Name = name,
            Description = description,
            AdditionalProperties = new Dictionary<string, object?>
            {
                [ConnectorKey] = context.ConnectorId,
                [RiskKey] = risk,
            },
        });
    }

    /// <summary>
    /// Presents a tool from elsewhere, such as an MCP server, under this connection's
    /// prefixed name with a risk. The inner function still calls the service by its own name.
    /// </summary>
    public static AIFunction Wrap(IConnectionContext context, AIFunction inner, string verb, ToolRisk risk)
    {
        var name = $"{context.ToolPrefix}_{verb}";
        if (!ValidName().IsMatch(name))
            throw new ArgumentException($"Tool name '{name}' is not valid.");

        return new TaggedFunction(inner, name, new Dictionary<string, object?>(inner.AdditionalProperties)
        {
            [ConnectorKey] = context.ConnectorId,
            [RiskKey] = risk,
        });
    }

    private sealed class TaggedFunction(AIFunction inner, string name, IReadOnlyDictionary<string, object?> properties)
        : DelegatingAIFunction(inner)
    {
        public override string Name => name;
        public override IReadOnlyDictionary<string, object?> AdditionalProperties => properties;
    }

    /// <summary>The risk a tool declared. A tool that declared none is treated as a write.</summary>
    public static ToolRisk RiskOf(AITool tool) =>
        tool.AdditionalProperties is { } props && props.TryGetValue(RiskKey, out var value) && value is ToolRisk risk
            ? risk
            : ToolRisk.Write;

    /// <summary>The failure shape every tool returns: structured, with a code the model can act on.</summary>
    public static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { error = code, message });

    public static string Ok(object value) => JsonSerializer.Serialize(value);

    [GeneratedRegex("^[a-z][a-z0-9_]{2,63}$")]
    private static partial Regex ValidName();
}
