#pragma warning disable MEAI001

using System.Text.Json;
using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Orchestration.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.McpServer;

/// <summary>
/// The tools an outside client can call, chosen by name from the ones this host registers.
/// Each goes through the same governance and tracing as when an agent calls it, and each
/// call is recorded in the run ledger. A tool that needs a person's approval is left out,
/// because an MCP call has nobody to ask.
/// </summary>
public sealed class ExposedToolSet
{
    public IReadOnlyList<AIFunction> Tools { get; }

    public IReadOnlyList<string> NotFound { get; }

    public ExposedToolSet(IServiceProvider services, McpServerOptions options)
    {
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger<ExposedToolSet>();
        var ledger = services.GetService<IRunLedger>();

        var available = services.GetServices<IReadOnlyList<AITool>>()
            .SelectMany(t => t)
            .OfType<AIFunction>()
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var chosen = new List<AIFunction>();
        var missing = new List<string>();

        foreach (var pattern in options.ExposedTools.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()))
        {
            var matches = available.Where(t => Matches(pattern, t.Name)).ToList();
            if (matches.Count == 0)
            {
                missing.Add(pattern);
                continue;
            }

            foreach (var tool in matches.Where(t => chosen.All(c => !c.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase))))
                chosen.Add(tool);
        }

        var wrapped = new List<AIFunction>();
        foreach (var tool in chosen)
        {
            if (tool is ApprovalRequiredAIFunction)
            {
                logger?.LogWarning("Tool {Tool} needs approval, which an MCP call cannot give, so it is not exposed", tool.Name);
                continue;
            }

            var governed = AgentPipeline.WrapTools(services, [tool])[0] as AIFunction ?? tool;
            wrapped.Add(new ExternalCallFunction(governed, ledger, options.MaxResultChars));
        }

        Tools = wrapped;
        NotFound = missing;

        if (missing.Count > 0)
            logger?.LogWarning("Agent:McpServer:ExposedTools names tools this host does not register: {Names}", string.Join(", ", missing));
        logger?.LogInformation("MCP server exposes {Count} tool(s): {Names}", wrapped.Count, string.Join(", ", wrapped.Select(t => t.Name)));
    }

    internal static bool Matches(string pattern, string name) =>
        pattern.EndsWith('*')
            ? name.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
            : name.Equals(pattern, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Records each call from an outside client as a run, with its arguments and a preview of the result.</summary>
internal sealed class ExternalCallFunction(AIFunction inner, IRunLedger? ledger, int maxResultChars) : DelegatingAIFunction(inner)
{
    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        object? result = null;
        var input = $"{Name}({Clip(SafeSerialize(arguments), 500)})";

        await RunTracking.RunAsync(ledger, new RunStart(RunSource.External, Name, input), async ct =>
        {
            result = await base.InvokeCoreAsync(arguments, ct);
            return Clip(result?.ToString() ?? string.Empty, 1000);
        }, cancellationToken);

        return Limit(result);
    }

    private object? Limit(object? result)
    {
        var text = result switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
            _ => null,
        };

        return text is not null && text.Length > maxResultChars
            ? text[..maxResultChars] + "\n[result cut at " + maxResultChars + " characters]"
            : result;
    }

    private static string SafeSerialize(AIFunctionArguments arguments)
    {
        try
        {
            return JsonSerializer.Serialize(arguments.ToDictionary(p => p.Key, p => p.Value));
        }
        catch (NotSupportedException)
        {
            return "[arguments could not be recorded]";
        }
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max] + "...";
}
