using AiAgentCanvas.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AiAgentCanvas.Capabilities.Rag;

public sealed record IngestBody(string? Source, string? Text, string? Tags);

public static class RagEndpoints
{
    /// <summary>
    /// The way documents get in and out of the index. Protect it with the platform's endpoint
    /// authorization: whoever can write here decides what the agents are told is true.
    /// </summary>
    public static RouteGroupBuilder MapRagEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/rag");

        group.MapGet("/documents", async (IDocumentIndex index, CancellationToken ct) =>
            Results.Json(await index.ListDocumentsAsync(ct)));

        group.MapPost("/documents", async (IngestBody body, RagIngestionService ingestion, CancellationToken ct) =>
        {
            try
            {
                return Results.Json(await ingestion.IngestAsync(body.Source ?? "", body.Text ?? "", body.Tags, ct));
            }
            catch (RagIngestionException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // The source is a query value and not a path segment, because it may hold slashes.
        group.MapDelete("/documents", async (string? source, RagIngestionService ingestion, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(source))
                return Results.BadRequest(new { error = "Give the source to delete as ?source=." });

            var removed = await ingestion.DeleteAsync(source.Trim(), ct);
            return removed == 0
                ? Results.NotFound(new { error = $"No document with source '{source}'." })
                : Results.Json(new { source = source.Trim(), removedChunks = removed });
        });

        return group;
    }
}
