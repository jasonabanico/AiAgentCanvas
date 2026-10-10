using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Orchestration.Skills;

/// <summary>
/// Offers the tools in the <see cref="DynamicToolRegistry"/> to an agent on each call, so a
/// tool that appears while the host runs, such as one from a connector or a server connected
/// in chat, reaches the agent without rebuilding it. The tools are added to the agent's own,
/// and a name the agent already has is not added twice.
/// </summary>
/// <remarks>
/// Each agent gets its own provider with its own filter, so an agent whose tool seed names
/// its tools sees only those, as it does for tools registered at startup.
/// </remarks>
public sealed class DynamicToolContextProvider : AIContextProvider
{
    private readonly DynamicToolRegistry _registry;
    private readonly Func<string, bool>? _include;

    /// <param name="registry">Where runtime tools are registered.</param>
    /// <param name="include">Tool names this agent may see. Null means all of them.</param>
    public DynamicToolContextProvider(DynamicToolRegistry registry, Func<string, bool>? include = null)
    {
        _registry = registry;
        _include = include;
    }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken)
    {
        var offered = _registry.GetAllTools()
            .Where(t => _include is null || _include(t.Name))
            .ToList();

        if (offered.Count == 0)
            return new ValueTask<AIContext>(new AIContext());

        // The agent merges what is returned into the tools it already has, so return only the
        // tools that are new to it.
        var names = new HashSet<string>(
            context.AIContext.Tools?.Select(t => t.Name) ?? [], StringComparer.Ordinal);

        var added = offered.Where(t => names.Add(t.Name)).ToList();
        return new ValueTask<AIContext>(new AIContext { Tools = added });
    }
}
