#pragma warning disable MEAI001

using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Capabilities.EpisodicMemory;

public static class EpisodicMemoryToolProvider
{
    /// <summary>
    /// Recall ranks by embedding similarity when <paramref name="embeddingGenerator"/>
    /// is supplied and by keyword match when it is not, so the capability works with
    /// or without an embedding model configured.
    /// </summary>
    public static IReadOnlyList<AITool> CreateTools(
        EpisodicMemoryStore store,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null,
        string agentName = "AiAgentCanvas")
    {
        return
        [
            AIFunctionFactory.Create(
                [Description("Search past episodes for goals similar to this one. Ranks by meaning when an embedding model is configured, by keyword otherwise")]
                async (string query, string? agentFilter, int? limit, CancellationToken ct) =>
                {
                    var take = limit ?? 5;
                    IReadOnlyList<Episode> results;

                    if (embeddingGenerator is not null && !string.IsNullOrWhiteSpace(query))
                    {
                        var embedding = await embeddingGenerator.GenerateVectorAsync(query, cancellationToken: ct);
                        results = store.SearchByEmbedding(embedding, agentFilter, take, query);
                    }
                    else
                    {
                        results = store.Search(query, agentFilter, take);
                    }

                    return Serialize(results);
                }, "search_memory"),

            AIFunctionFactory.Create(
                [Description("Get the most recent episodes from memory")]
                (string? agentFilter, int? limit) => Serialize(store.GetRecent(agentFilter, limit ?? 5)),
                "recall_recent_memory"),

            AIFunctionFactory.Create(
                [Description("Record a completed episode for future recall. Set importance from 0 to 1: use a high value for a hard-won lesson or a failure worth avoiding, a low value for a routine lookup. Low-importance episodes are not stored")]
                async (string goal, string summary, string outcome, string[] toolsUsed, int turnCount, double? importance, CancellationToken ct) =>
                {
                    var episode = new Episode
                    {
                        AgentName = agentName,
                        Goal = goal,
                        Summary = summary,
                        Outcome = outcome,
                        ToolsUsed = toolsUsed.ToList(),
                        TurnCount = turnCount,
                        Importance = Math.Clamp(importance ?? 0.5, 0.0, 1.0),
                    };

                    if (embeddingGenerator is not null)
                    {
                        var vector = await embeddingGenerator.GenerateVectorAsync($"{goal}\n{summary}", cancellationToken: ct);
                        episode.Embedding = vector.ToArray();
                    }

                    var saved = store.Save(episode);
                    return JsonSerializer.Serialize(saved
                        ? new { saved = true, episode.Id, note = (string?)null }
                        : new { saved = false, episode.Id, note = (string?)"Importance was below the store's threshold, so this episode was not kept." });
                }, "save_to_memory"),
        ];
    }

    private static string Serialize(IReadOnlyList<Episode> episodes) =>
        JsonSerializer.Serialize(episodes.Select(e => new
        {
            e.Id,
            e.AgentName,
            e.Goal,
            e.Summary,
            e.Outcome,
            e.ToolsUsed,
            e.TurnCount,
            e.Importance,
            CompletedAt = e.CompletedAt.ToString("g"),
        }));
}
