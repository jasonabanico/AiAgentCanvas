using System.Text.Json;
using System.Text.Json.Serialization;
using AiAgentCanvas.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AiAgentCanvas.Capabilities.RunLedger;

public static class RunLedgerEndpoints
{
    // Status and source go out as names so a viewer does not need the enum's numbering.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static RouteGroupBuilder MapRunLedgerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/runs");

        group.MapGet("/", (
            IRunLedger ledger,
            int? limit,
            string? agent,
            string? status,
            string? source,
            string? triggerId,
            int? hours,
            bool? includeChildren) =>
        {
            var runs = ledger.Recent(new RunQuery(
                Limit: limit ?? 50,
                Since: hours is > 0 ? DateTimeOffset.UtcNow.AddHours(-hours.Value) : null,
                AgentName: agent,
                Status: Enum.TryParse<RunStatus>(status, true, out var s) ? s : null,
                Source: Enum.TryParse<RunSource>(source, true, out var src) ? src : null,
                TriggerId: triggerId,
                TopLevelOnly: includeChildren != true));

            return Results.Json(runs, Json);
        });

        group.MapGet("/totals", (IRunLedger ledger, int? hours, string? agent, string? triggerId) =>
            Results.Json(ledger.Totals(DateTimeOffset.UtcNow.AddHours(-(hours ?? 24)), agent, triggerId), Json));

        group.MapGet("/{id}", (IRunLedger ledger, string id) =>
        {
            var run = ledger.Get(id);
            if (run is null)
                return Results.NotFound(new { error = "Run not found" });

            return Results.Json(new { run, children = ledger.Children(id) }, Json);
        });

        return group;
    }
}
