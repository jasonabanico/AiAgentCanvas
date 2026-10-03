namespace AiAgentCanvas.Connections;

public enum OAuthClientAuth
{
    /// <summary>The client id and secret are sent in the request body.</summary>
    Body,

    /// <summary>The client id and secret are sent as HTTP basic authentication.</summary>
    Basic,
}

/// <summary>
/// How to talk to one provider's authorization server. Each URL is the provider's
/// documented authorize and token endpoint; nothing here is inferred.
/// </summary>
public sealed record OAuthProviderDefinition(
    string Slug,
    string Name,
    string AuthorizeUrl,
    string TokenUrl,
    IReadOnlyList<string> DefaultScopes,
    string ScopeSeparator = " ",
    bool Pkce = true,
    OAuthClientAuth ClientAuth = OAuthClientAuth.Body,
    IReadOnlyDictionary<string, string>? ExtraAuthorizeParams = null);

public sealed class OAuthProviderRegistry
{
    private readonly Dictionary<string, OAuthProviderDefinition> _providers = new(StringComparer.OrdinalIgnoreCase);

    public OAuthProviderRegistry(ConnectionsOptions options)
    {
        foreach (var provider in BuiltIn())
            _providers[provider.Slug] = provider;

        foreach (var (slug, config) in options.OAuthProviders)
        {
            if (string.IsNullOrWhiteSpace(config.AuthorizeUrl) || string.IsNullOrWhiteSpace(config.TokenUrl))
            {
                throw new InvalidOperationException(
                    $"Connections:OAuthProviders:{slug} needs both AuthorizeUrl and TokenUrl.");
            }

            _providers[slug] = new OAuthProviderDefinition(
                slug,
                config.Name ?? slug,
                config.AuthorizeUrl,
                config.TokenUrl,
                config.DefaultScopes,
                config.ScopeSeparator,
                config.Pkce,
                config.ClientAuth,
                config.ExtraAuthorizeParams);
        }
    }

    public OAuthProviderDefinition? Get(string slug) => _providers.GetValueOrDefault(slug);

    public IReadOnlyList<OAuthProviderDefinition> List() => _providers.Values.OrderBy(p => p.Slug).ToList();

    private static IEnumerable<OAuthProviderDefinition> BuiltIn()
    {
        yield return new OAuthProviderDefinition(
            "google", "Google",
            "https://accounts.google.com/o/oauth2/v2/auth",
            "https://oauth2.googleapis.com/token",
            [],
            // Without these Google returns no refresh token when an account is connected again.
            ExtraAuthorizeParams: new Dictionary<string, string> { ["access_type"] = "offline", ["prompt"] = "consent" });

        yield return new OAuthProviderDefinition(
            "microsoft", "Microsoft",
            "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
            "https://login.microsoftonline.com/common/oauth2/v2.0/token",
            // Without offline_access Microsoft issues no refresh token.
            ["offline_access"]);

        yield return new OAuthProviderDefinition(
            "slack", "Slack",
            "https://slack.com/oauth/v2/authorize",
            "https://slack.com/api/oauth.v2.access",
            [],
            ScopeSeparator: ",",
            Pkce: false);

        yield return new OAuthProviderDefinition(
            "github", "GitHub",
            "https://github.com/login/oauth/authorize",
            "https://github.com/login/oauth/access_token",
            [],
            Pkce: false);

        yield return new OAuthProviderDefinition(
            "notion", "Notion",
            "https://api.notion.com/v1/oauth/authorize",
            "https://api.notion.com/v1/oauth/token",
            [],
            Pkce: false,
            ClientAuth: OAuthClientAuth.Basic,
            ExtraAuthorizeParams: new Dictionary<string, string> { ["owner"] = "user" });

        yield return new OAuthProviderDefinition(
            "hubspot", "HubSpot",
            "https://app.hubspot.com/oauth/authorize",
            "https://api.hubapi.com/oauth/v1/token",
            [],
            Pkce: false);
    }
}
