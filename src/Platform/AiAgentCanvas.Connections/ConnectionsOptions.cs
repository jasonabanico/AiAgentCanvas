namespace AiAgentCanvas.Connections;

public sealed class ConnectionsOptions
{
    public const string SectionName = "Connections";

    public string DatabasePath { get; set; } = "connections.db";

    /// <summary>
    /// Where the Data Protection key ring is stored. Whoever can read this folder and the
    /// database can read every secret, so keep it outside the web root and out of source
    /// control. On Windows the keys are also wrapped with DPAPI unless that is switched off.
    /// </summary>
    public string KeyRingPath { get; set; } = "data-protection-keys";

    public bool ProtectKeysWithDpapi { get; set; } = true;

    /// <summary>
    /// The address the browser reaches this host at, such as <c>https://agents.example.com</c>.
    /// The OAuth redirect address is built from it, not from the request, so a forged
    /// Host header cannot send an authorization code elsewhere. Several providers require
    /// a public https address.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>Where the browser lands after connecting an account. Must be a path on this host.</summary>
    public string ReturnUrl { get; set; } = "/";

    /// <summary>A token this close to expiry is refreshed before it is handed out.</summary>
    public int RefreshSkewSeconds { get; set; } = 120;

    /// <summary>How often the background sweep looks for tokens about to expire.</summary>
    public int SweepIntervalSeconds { get; set; } = 60;

    /// <summary>The sweep refreshes tokens that expire within this many minutes.</summary>
    public int SweepWindowMinutes { get; set; } = 10;

    public int HttpTimeoutSeconds { get; set; } = 15;

    /// <summary>Client credentials by provider slug. Prefer user secrets or environment variables.</summary>
    public Dictionary<string, OAuthAppConfig> OAuthApps { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Extra or overriding provider definitions by slug.</summary>
    public Dictionary<string, OAuthProviderConfig> OAuthProviders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class OAuthAppConfig
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

public sealed class OAuthProviderConfig
{
    public string? Name { get; set; }
    public string AuthorizeUrl { get; set; } = string.Empty;
    public string TokenUrl { get; set; } = string.Empty;
    public List<string> DefaultScopes { get; set; } = [];
    public string ScopeSeparator { get; set; } = " ";
    public bool Pkce { get; set; } = true;
    public OAuthClientAuth ClientAuth { get; set; } = OAuthClientAuth.Body;
    public Dictionary<string, string> ExtraAuthorizeParams { get; set; } = [];
}
