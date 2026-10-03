using AiAgentCanvas.Connections;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AiAgentCanvas.Connectors;

public static class ConnectorEndpoints
{
    public const string WebhookPathTemplate = "/api/connectors/{connectionId}/webhook";

    /// <summary>The catalog of installed connectors and the state of each connection. Protect it with the platform's endpoint authorization.</summary>
    public static RouteGroupBuilder MapConnectorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/connectors");

        group.MapGet("/", (ConnectorRegistry registry) =>
            Results.Ok(registry.List().Select(d => new
            {
                d.Id,
                d.DisplayName,
                d.Category,
                Auth = d.Auth.ToString(),
                d.Scopes,
                Capabilities = d.Capabilities.ToString(),
                d.OAuthProvider,
            })));

        group.MapGet("/connections", (ConnectorHost host, ConnectionsOptions connections) =>
            Results.Ok(host.Snapshot().Select(i => new
            {
                i.ConnectionId,
                i.ConnectorId,
                i.Label,
                i.ToolPrefix,
                State = i.State.ToString(),
                i.Detail,
                i.ToolCount,
                Capabilities = i.Capabilities.ToString(),
                i.CheckedAt,
                WebhookUrl = i.Capabilities.HasFlag(ConnectorCapabilities.Events)
                    ? WebhookUrl(connections, i.ConnectionId)
                    : null,
            })));

        group.MapPost("/reconcile", async (ConnectorHost host, HttpContext context) =>
        {
            await host.ReconcileAsync(context.RequestAborted);
            return Results.Ok(host.Snapshot());
        });

        group.MapPost("/connections/{connectionId}/check", async (string connectionId, ConnectorHost host, HttpContext context) =>
        {
            var info = await host.CheckNowAsync(connectionId, context.RequestAborted);
            return info is null ? Results.NotFound(new { error = "No such connection." }) : Results.Ok(info);
        });

        return group;
    }

    /// <summary>
    /// Where services deliver webhooks. It stays outside the endpoint authorization because
    /// the sender carries no credentials of ours; the connector checks the sender's own
    /// signature, and a request that fails the check is refused before anything is read.
    /// </summary>
    public static IEndpointConventionBuilder MapConnectorWebhook(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost(WebhookPathTemplate, async (
            string connectionId,
            HttpContext context,
            ConnectorHost host,
            ConnectorOptions options,
            ConnectionsOptions connections) =>
        {
            var limit = Math.Max(1024, options.MaxWebhookBodyBytes);
            if (context.Request.ContentLength > limit)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            var body = await ReadBoundedAsync(context.Request.Body, limit, context.RequestAborted);
            if (body is null)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in context.Request.Headers)
                headers[header.Key] = header.Value.ToString();

            // The sender signed the address it was given, so rebuild that address from
            // configuration and not from headers a proxy or a client could change.
            var url = WebhookUrl(connections, connectionId, context.Request);

            var outcome = await host.HandleWebhookAsync(connectionId, new WebhookRequest(url, headers, body), context.RequestAborted);

            switch (outcome)
            {
                case WebhookOutcome.Accepted:
                    return Results.Ok();
                case WebhookOutcome.Unauthorized:
                    return Results.StatusCode(StatusCodes.Status401Unauthorized);
                case WebhookOutcome.BadRequest:
                    return Results.BadRequest();
                case WebhookOutcome.Busy:
                    context.Response.Headers.RetryAfter = "60";
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                default:
                    return Results.NotFound();
            }
        });

    internal static string WebhookUrl(ConnectionsOptions connections, string connectionId, HttpRequest? request = null)
    {
        var path = WebhookPathTemplate.Replace("{connectionId}", Uri.EscapeDataString(connectionId));
        if (!string.IsNullOrWhiteSpace(connections.PublicBaseUrl))
            return connections.PublicBaseUrl.TrimEnd('/') + path;

        return request is null ? path : $"{request.Scheme}://{request.Host}{path}";
    }

    private static async Task<byte[]?> ReadBoundedAsync(Stream stream, int limit, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > limit)
                return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
