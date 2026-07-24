#pragma warning disable MAAI001

using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.EpisodicMemory;

public sealed class EpisodicMemoryContextProvider : AIContextProvider
{
    private readonly EpisodicMemoryStore _store;
    private readonly ILogger<EpisodicMemoryContextProvider> _logger;
    private readonly int _maxEpisodes;

    public EpisodicMemoryContextProvider(
        EpisodicMemoryStore store,
        ILogger<EpisodicMemoryContextProvider> logger,
        int maxEpisodes = 3)
    {
        _store = store;
        _logger = logger;
        _maxEpisodes = maxEpisodes;
    }

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken)
    {
        var recent = _store.GetRecent(limit: _maxEpisodes);
        if (recent.Count == 0)
            return new ValueTask<AIContext>(context.AIContext);

        var sb = new StringBuilder();
        sb.AppendLine("\n## Episodic Memory (recent sessions)");
        foreach (var ep in recent)
        {
            sb.AppendLine($"- [{ep.CompletedAt:g}] Goal: {ep.Goal}");
            sb.AppendLine($"  Outcome: {ep.Outcome} | Tools: {string.Join(", ", ep.ToolsUsed)} | Turns: {ep.TurnCount}");
            if (!string.IsNullOrWhiteSpace(ep.Summary))
                sb.AppendLine($"  Summary: {ep.Summary}");
        }

        context.AIContext.Instructions += sb.ToString();
        _logger.LogDebug("Injected {Count} episodes into context", recent.Count);

        return new ValueTask<AIContext>(context.AIContext);
    }
}
