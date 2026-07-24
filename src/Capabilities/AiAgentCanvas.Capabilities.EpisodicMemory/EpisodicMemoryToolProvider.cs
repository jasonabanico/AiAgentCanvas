#pragma warning disable MEAI001

using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Capabilities.EpisodicMemory;

public static class EpisodicMemoryToolProvider
{
    public static IReadOnlyList<AITool> CreateTools(EpisodicMemoryStore store)
    {
        return
        [
            AIFunctionFactory.Create(
                [Description("Search past episodes from memory for similar goals or outcomes")]
                (string query, string? agentName, int? limit) =>
                {
                    var results = store.Search(query, agentName, limit ?? 5);
                    return JsonSerializer.Serialize(results.Select(e => new
                    {
                        e.Id, e.AgentName, e.Goal, e.Summary, e.Outcome,
                        e.ToolsUsed, e.TurnCount, CompletedAt = e.CompletedAt.ToString("g"),
                    }));
                }, "search_memory"),

            AIFunctionFactory.Create(
                [Description("Get the most recent episodes from memory")]
                (string? agentName, int? limit) =>
                {
                    var results = store.GetRecent(agentName, limit ?? 5);
                    return JsonSerializer.Serialize(results.Select(e => new
                    {
                        e.Id, e.AgentName, e.Goal, e.Summary, e.Outcome,
                        e.ToolsUsed, e.TurnCount, CompletedAt = e.CompletedAt.ToString("g"),
                    }));
                }, "recall_recent_memory"),

            AIFunctionFactory.Create(
                [Description("Record an episode to memory for future recall")]
                (string goal, string summary, string outcome, string[] toolsUsed, int turnCount) =>
                {
                    var episode = new Episode
                    {
                        AgentName = "AiAgentCanvas",
                        Goal = goal,
                        Summary = summary,
                        Outcome = outcome,
                        ToolsUsed = toolsUsed.ToList(),
                        TurnCount = turnCount,
                    };
                    store.Save(episode);
                    return JsonSerializer.Serialize(new { saved = true, episode.Id });
                }, "save_to_memory"),
        ];
    }
}
