using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AiAgentCanvas.Capabilities.EpisodicMemory;

public static class EpisodicMemoryEndpoints
{
    /// <summary>
    /// Lets a person see what the agents remember and delete it. Stored memories can hold
    /// personal details a user shared, so inspecting and deleting them is part of running the
    /// feature. Protect it with the platform's endpoint authorization.
    /// </summary>
    public static RouteGroupBuilder MapEpisodicMemoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/memory");

        group.MapGet("/episodes", (EpisodicMemoryStore store, string? agent, int? limit, int? offset) =>
            Results.Json(store.List(agent, limit ?? 50, offset ?? 0).Select(Describe)));

        group.MapGet("/episodes/{id}", (EpisodicMemoryStore store, string id) =>
            store.Get(id) is { } episode
                ? Results.Json(Describe(episode))
                : Results.NotFound(new { error = "No such episode." }));

        group.MapDelete("/episodes/{id}", (EpisodicMemoryStore store, string id) =>
            store.Delete(id)
                ? Results.Json(new { forgotten = id })
                : Results.NotFound(new { error = "No such episode." }));

        // Wiping everything takes a deliberate flag, so a stray request cannot do it.
        group.MapDelete("/episodes", (EpisodicMemoryStore store, string? agent, bool? confirm) =>
            confirm == true
                ? Results.Json(new { forgotten = store.DeleteAll(agent) })
                : Results.BadRequest(new { error = "This deletes every episode" + (agent is null ? "" : $" of agent '{agent}'") + ". Add ?confirm=true to proceed." }));

        return group;
    }

    private static object Describe(Episode e) => new
    {
        e.Id,
        e.AgentName,
        e.Goal,
        e.Summary,
        e.Outcome,
        e.ToolsUsed,
        e.TurnCount,
        e.Importance,
        e.RelevanceScore,
        e.RecallCount,
        e.StartedAt,
        e.CompletedAt,
        Embedded = e.Embedding is { Length: > 0 },
    };
}
