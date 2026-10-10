using System.Text.Json;
using AiAgentCanvas.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AiAgentCanvas.Capabilities.StructuredOutput;

public sealed record StructuredRequestBody(
    string Instruction,
    string? Input,
    string? SchemaName,
    JsonElement? Schema,
    int? MaxAttempts);

public static class StructuredEndpoints
{
    public static RouteGroupBuilder MapStructuredEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/structured");

        group.MapGet("/schemas", (ISchemaCatalog catalog) => Results.Ok(new { schemas = catalog.Names }));

        group.MapPost("/extract", async (
            StructuredRequestBody body,
            IStructuredResponder responder,
            ISchemaCatalog catalog,
            StructuredOptions options,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Instruction))
                return Results.BadRequest(new { error = "instruction is required." });

            var schemaText = body.Schema is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } s ? s.GetRawText() : null;
            if (!StructuredToolProvider.TryResolveSchema(catalog, options, body.SchemaName, schemaText, out var schema, out var description, out var name, out var error))
                return Results.BadRequest(new { error });

            var result = await responder.RespondAsync(
                new StructuredRequest(body.Instruction, body.Input, schema, name, description, null, body.MaxAttempts ?? 3), ct);

            // A well-formed request the model could not satisfy is not a client error.
            return Results.Ok(new { success = result.Success, value = result.Value, errors = result.Errors, attempts = result.Attempts });
        });

        return group;
    }
}
