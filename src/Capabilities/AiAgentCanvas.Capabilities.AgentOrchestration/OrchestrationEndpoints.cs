using AiAgentCanvas.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AiAgentCanvas.Capabilities.AgentOrchestration;

public sealed record StartOrchestrationBody(
    string Kind,
    string Task,
    List<string>? Agents,
    string? Lead,
    int? MaxRounds,
    bool? RequireSignoff);

public sealed record RespondBody(bool Approve, string? Feedback);

public static class OrchestrationEndpoints
{
    /// <summary>
    /// The management surface, and the only way to answer a run that is waiting for a person.
    /// Protect it with the platform's endpoint authorization.
    /// </summary>
    public static RouteGroupBuilder MapOrchestrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/orchestrations");

        group.MapGet("/", (OrchestrationStore store, string? status, int? limit) =>
        {
            OrchestrationStatus? filter = Enum.TryParse<OrchestrationStatus>(status, true, out var s) ? s : null;
            return Results.Json(store.List(limit ?? 50, filter), OrchestrationToolProvider.Json);
        });

        group.MapGet("/{id}", (OrchestrationStore store, string id) =>
        {
            var run = store.Get(id);
            return run is null ? Results.NotFound(new { error = "No such run." }) : Results.Json(run, OrchestrationToolProvider.Json);
        });

        group.MapPost("/", async (StartOrchestrationBody body, OrchestrationRunner runner, CancellationToken ct) =>
        {
            if (!Enum.TryParse<OrchestrationKind>(body.Kind, true, out var kind))
                return Results.BadRequest(new { error = "kind must be GroupChat, Handoff or Magentic." });

            try
            {
                var run = await runner.StartAsync(
                    new OrchestrationSpec(kind, body.Task, body.Agents ?? [], body.Lead, body.MaxRounds, body.RequireSignoff ?? true), ct);
                return Results.Json(run, OrchestrationToolProvider.Json);
            }
            catch (OrchestrationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPost("/{id}/respond", async (string id, RespondBody body, OrchestrationRunner runner, CancellationToken ct) =>
        {
            try
            {
                var run = await runner.RespondAsync(id, new OrchestrationResponse(body.Approve, body.Feedback), ct);
                return Results.Json(run, OrchestrationToolProvider.Json);
            }
            catch (OrchestrationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPost("/{id}/resume", async (string id, OrchestrationRunner runner, CancellationToken ct) =>
        {
            try
            {
                return Results.Json(await runner.ResumeAsync(id, ct), OrchestrationToolProvider.Json);
            }
            catch (OrchestrationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPost("/{id}/cancel", (string id, OrchestrationRunner runner) =>
        {
            var run = runner.Cancel(id);
            return run is null ? Results.NotFound(new { error = "No such run." }) : Results.Json(run, OrchestrationToolProvider.Json);
        });

        return group;
    }
}
