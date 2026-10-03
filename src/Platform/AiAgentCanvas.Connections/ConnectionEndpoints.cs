using AiAgentCanvas.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AiAgentCanvas.Connections;

public sealed record StartOAuthBody(
    string ConnectorId,
    string? Label,
    List<string>? Scopes,
    string? ReturnUrl,
    string? ReconnectConnectionId);

public sealed record SaveOAuthAppBody(string ClientId, string ClientSecret);

public sealed record CreateKeyConnectionBody(
    string ConnectorId,
    string? Label,
    string Kind,
    string? ApiKey,
    string? KeyId,
    string? KeySecret,
    Dictionary<string, string>? Extras,
    Dictionary<string, string>? Settings);

public static class ConnectionEndpoints
{
    /// <summary>
    /// The management surface. Protect it with the platform's endpoint authorization:
    /// anyone who can call it can add or remove accounts the agents act through.
    /// Responses never contain a secret.
    /// </summary>
    public static RouteGroupBuilder MapConnectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/connections");

        group.MapGet("/", (ConnectionStore store) =>
            Results.Ok(store.List().Select(Describe)));

        group.MapGet("/oauth/providers", (OAuthProviderRegistry providers, OAuthService oauth) =>
            Results.Ok(providers.List().Select(p => new
            {
                p.Slug,
                p.Name,
                AppConfigured = oauth.ResolveApp(p.Slug) is not null,
                p.Pkce,
                DefaultScopes = p.DefaultScopes,
            })));

        group.MapPost("/oauth/{provider}/start", (string provider, StartOAuthBody body, OAuthService oauth) =>
        {
            try
            {
                var result = oauth.Start(new OAuthStartRequest(
                    provider, body.ConnectorId, body.Label ?? body.ConnectorId, body.Scopes, body.ReturnUrl, body.ReconnectConnectionId));
                return Results.Ok(new { authorizeUrl = result.AuthorizeUrl });
            }
            catch (OAuthException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPut("/oauth/apps/{provider}", (string provider, SaveOAuthAppBody body, ConnectionStore store, OAuthProviderRegistry providers) =>
        {
            if (providers.Get(provider) is null)
                return Results.NotFound(new { error = $"Unknown provider '{provider}'." });
            if (string.IsNullOrWhiteSpace(body.ClientId) || string.IsNullOrWhiteSpace(body.ClientSecret))
                return Results.BadRequest(new { error = "Both clientId and clientSecret are required." });

            store.SaveOAuthApp(new OAuthApp(provider, body.ClientId.Trim(), body.ClientSecret.Trim()));
            return Results.Ok(new { saved = true, provider });
        });

        group.MapPost("/key", (CreateKeyConnectionBody body, ConnectionStore store) =>
        {
            if (!Enum.TryParse<AuthKind>(body.Kind, true, out var kind) || kind is not (AuthKind.ApiKey or AuthKind.KeyPair))
                return Results.BadRequest(new { error = "kind must be ApiKey or KeyPair. Use the OAuth start endpoint for OAuth." });
            if (string.IsNullOrWhiteSpace(body.ConnectorId))
                return Results.BadRequest(new { error = "connectorId is required." });

            var payload = new SecretPayload { Extras = body.Extras ?? [] };
            if (kind == AuthKind.ApiKey)
            {
                if (string.IsNullOrWhiteSpace(body.ApiKey))
                    return Results.BadRequest(new { error = "apiKey is required for an ApiKey connection." });
                payload.ApiKey = body.ApiKey;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(body.KeyId) || string.IsNullOrWhiteSpace(body.KeySecret))
                    return Results.BadRequest(new { error = "keyId and keySecret are required for a KeyPair connection." });
                payload.KeyId = body.KeyId;
                payload.KeySecret = body.KeySecret;
            }

            var connection = new Connection
            {
                ConnectorId = body.ConnectorId.Trim(),
                Label = string.IsNullOrWhiteSpace(body.Label) ? body.ConnectorId.Trim() : body.Label.Trim(),
                Auth = kind,
                Settings = body.Settings ?? [],
            };
            store.Save(connection);
            store.SaveSecret(connection.Id, payload);

            return Results.Created($"/api/connections/{connection.Id}", Describe(connection));
        });

        group.MapDelete("/{id}", (string id, ConnectionStore store) =>
            store.Delete(id) ? Results.Ok(new { deleted = true, id }) : Results.NotFound(new { error = "No such connection." }));

        return group;
    }

    /// <summary>
    /// The address the provider sends the browser back to. It is deliberately left outside
    /// the endpoint authorization: the browser arrives from another site carrying no
    /// credentials, and the encrypted, time-limited state is what proves this host
    /// started the flow. It completes only flows this host began and exposes no data.
    /// </summary>
    public static IEndpointConventionBuilder MapConnectionCallback(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet(OAuthService.CallbackPath, async (
            HttpContext context,
            OAuthService oauth,
            string? code,
            string? state,
            string? error,
            string? error_description) =>
        {
            var result = await oauth.CompleteAsync(code, state, error, error_description, context.RequestAborted);

            var separator = result.ReturnUrl.Contains('?') ? "&" : "?";
            var target = result.ReturnUrl + separator
                + $"connection={(result.Success ? "ok" : "error")}"
                + (result.ConnectionId is null ? "" : $"&id={Uri.EscapeDataString(result.ConnectionId)}")
                + $"&message={Uri.EscapeDataString(result.Message)}";

            return Results.Redirect(target);
        });

    private static object Describe(Connection c) => new
    {
        c.Id,
        c.ConnectorId,
        c.Label,
        c.OwnerScope,
        Auth = c.Auth.ToString(),
        Status = c.Status.ToString(),
        c.StatusDetail,
        c.Scopes,
        c.Settings,
        c.ExpiresAt,
        c.CreatedAt,
        c.UpdatedAt,
    };
}
