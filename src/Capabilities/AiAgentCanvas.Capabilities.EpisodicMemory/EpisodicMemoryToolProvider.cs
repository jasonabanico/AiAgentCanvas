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

                    // An episode that is asked for again is still useful, so it is kept fresh.
                    store.Reinforce(results.Select(r => r.Id));
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

                    var written = store.Store(episode);
                    return JsonSerializer.Serialize(new
                    {
                        saved = written.Stored,
                        written.Id,
                        merged = written.Merged,
                        note = !written.Stored
                            ? "Importance was below the store's threshold, so this episode was not kept."
                            : written.Merged
                                ? "A very similar episode was already stored, so it was updated with this one and no second copy was added."
                                : (string?)null,
                    });
                }, "save_to_memory"),

            AIFunctionFactory.Create(
                [Description("Forget one stored episode by its id, for example when the user asks you to forget something. Search or list memory first to find the id")]
                (string episodeId) => JsonSerializer.Serialize(store.Delete(episodeId)
                    ? new { forgotten = true, id = episodeId, note = (string?)null }
                    : new { forgotten = false, id = episodeId, note = (string?)"No stored episode has that id." }),
                "forget_memory"),
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
            e.RecallCount,
            CompletedAt = e.CompletedAt.ToString("g"),
        }));
}
