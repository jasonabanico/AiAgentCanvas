using System.Text;
using System.Text.Json;

namespace AiAgentCanvas.Connections;

/// <summary>The fields of a token endpoint reply that matter here, across the providers' variations.</summary>
public sealed class OAuthTokenResponse
{
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public int? ExpiresInSeconds { get; init; }
    public IReadOnlyList<string> Scopes { get; init; } = [];
    public string? Error { get; init; }
    public string? ErrorDescription { get; init; }

    public bool IsSuccess => Error is null && !string.IsNullOrEmpty(AccessToken);

    /// <summary>The provider says the refresh token or code is no longer valid.</summary>
    public bool IsInvalidGrant => string.Equals(Error, "invalid_grant", StringComparison.OrdinalIgnoreCase);

    public static OAuthTokenResponse Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new OAuthTokenResponse { Error = "invalid_response", ErrorDescription = "the token endpoint did not return an object" };

            // Slack answers 200 with ok:false and an error code instead of an HTTP error.
            var slackFailed = root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False;
            var error = String(root, "error") ?? (slackFailed ? "unknown_error" : null);

            // Slack nests a user token under authed_user when only user scopes were requested.
            var access = String(root, "access_token");
            if (access is null && root.TryGetProperty("authed_user", out var user) && user.ValueKind == JsonValueKind.Object)
                access = String(user, "access_token");

            var expires = root.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var seconds) ? seconds : (int?)null;

            return new OAuthTokenResponse
            {
                AccessToken = access,
                RefreshToken = String(root, "refresh_token"),
                ExpiresInSeconds = expires,
                Scopes = SplitScopes(String(root, "scope")),
                Error = error,
                ErrorDescription = String(root, "error_description"),
            };
        }
        catch (JsonException)
        {
            return new OAuthTokenResponse { Error = "invalid_response", ErrorDescription = "the token endpoint did not return JSON" };
        }
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IReadOnlyList<string> SplitScopes(string? scope) =>
        string.IsNullOrWhiteSpace(scope)
            ? []
            : scope.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

internal static class OAuthRequests
{
    public static string BasicAuthorization(string clientId, string clientSecret) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Uri.EscapeDataString(clientId)}:{Uri.EscapeDataString(clientSecret)}"));

    public static Dictionary<string, string> ClientFields(OAuthProviderDefinition provider, OAuthApp app)
    {
        // Basic authentication carries the client credentials in a header, so they are
        // left out of the body.
        return provider.ClientAuth == OAuthClientAuth.Basic
            ? []
            : new Dictionary<string, string> { ["client_id"] = app.ClientId, ["client_secret"] = app.ClientSecret };
    }
}
