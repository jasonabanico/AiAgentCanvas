namespace AiAgentCanvas.Authentication;

/// <summary>
/// Authentication configuration for the platform's HTTP surface. The platform is
/// general purpose, so the scheme is chosen by configuration rather than compiled
/// in, and more than one can run at once.
/// </summary>
public sealed class AgentAuthenticationOptions
{
    public const string SectionName = "Authentication";

    /// <summary>
    /// Off by default so a fresh clone runs locally without setup. The Host logs a
    /// prominent warning on every start while it is off, because the agent it
    /// fronts runs shell commands and schedules unattended work.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Schemes to register, by name, in order. A request satisfying any one of them
    /// is authenticated. Built-in names are <c>ApiKey</c> and <c>JwtBearer</c>;
    /// register more by adding an <see cref="IAgentAuthenticationScheme"/>.
    /// </summary>
    public List<string> Schemes { get; set; } = [];

    /// <summary>
    /// Endpoint keys left open when authentication is on. <c>health</c> is open by
    /// default so liveness probes keep working.
    /// </summary>
    public List<string> AllowAnonymous { get; set; } = ["health"];

    /// <summary>
    /// Browser origins allowed to call the API. Empty keeps the permissive
    /// any-origin policy, which cannot be combined with credentials, so set this
    /// whenever <see cref="Enabled"/> is true and a browser is the caller.
    /// </summary>
    public List<string> AllowedOrigins { get; set; } = [];

    public ApiKeyOptions ApiKey { get; set; } = new();
    public JwtBearerSchemeOptions JwtBearer { get; set; } = new();

    public bool IsAnonymous(string endpointKey) =>
        AllowAnonymous.Contains(endpointKey, StringComparer.OrdinalIgnoreCase);
}

public sealed class ApiKeyOptions
{
    public string HeaderName { get; set; } = "X-API-Key";

    /// <summary>
    /// Accepted keys. Supply through user secrets, environment variables or a
    /// secret store, never a checked-in appsettings file.
    /// </summary>
    public List<string> Keys { get; set; } = [];

    /// <summary>Name recorded as the caller identity when a key matches.</summary>
    public string PrincipalName { get; set; } = "api-key-client";
}

public sealed class JwtBearerSchemeOptions
{
    /// <summary>
    /// OIDC authority. For Microsoft Entra ID this is
    /// <c>https://login.microsoftonline.com/{tenant}/v2.0</c>. Auth0, Okta and
    /// Keycloak work the same way.
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>Expected audience, usually the API's application or client id.</summary>
    public string? Audience { get; set; }

    /// <summary>Leave true outside local development.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Scopes a caller must present. Empty means any validly signed token for the
    /// audience is accepted.
    /// </summary>
    public List<string> RequiredScopes { get; set; } = [];
}
