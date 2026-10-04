using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiAgentCanvas.Authentication.Schemes;

/// <summary>
/// Shared-secret header authentication. Suits machine callers such as the
/// event-trigger webhooks, where there is no user to sign in. It carries no user
/// identity, so agent sessions isolate per key rather than per person.
/// </summary>
public sealed class ApiKeyScheme : IAgentAuthenticationScheme
{
    public const string SchemeName = "ApiKey";

    public string Name => SchemeName;

    public void Register(AuthenticationBuilder builder, AgentAuthenticationOptions options) =>
        builder.AddScheme<ApiKeySchemeOptions, ApiKeyHandler>(SchemeName, schemeOptions =>
        {
            schemeOptions.HeaderName = options.ApiKey.HeaderName;
            schemeOptions.Keys = options.ApiKey.Keys;
            schemeOptions.PrincipalName = options.ApiKey.PrincipalName;
        });
}

public sealed class ApiKeySchemeOptions : AuthenticationSchemeOptions
{
    public string HeaderName { get; set; } = "X-API-Key";
    public List<string> Keys { get; set; } = [];
    public string PrincipalName { get; set; } = "api-key-client";
}

public sealed class ApiKeyHandler : AuthenticationHandler<ApiKeySchemeOptions>
{
    public ApiKeyHandler(
        IOptionsMonitor<ApiKeySchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Options.Keys.Count == 0)
        {
            // Configuring the scheme with no keys would otherwise let every request
            // through, which is worse than having no authentication at all because
            // it looks protected.
            Logger.LogError("ApiKey scheme is registered with no keys configured. Every request will be rejected.");
            return Task.FromResult(AuthenticateResult.Fail("No API keys are configured."));
        }

        if (!Request.Headers.TryGetValue(Options.HeaderName, out var provided) || provided.Count == 0)
            return Task.FromResult(AuthenticateResult.NoResult());

        var presented = provided.ToString();
        if (!Options.Keys.Any(key => FixedTimeEquals(key, presented)))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, Options.PrincipalName), new Claim("auth_scheme", ApiKeyScheme.SchemeName)],
            ApiKeyScheme.SchemeName);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyScheme.SchemeName)));
    }

    /// <summary>
    /// Compares in time independent of how many leading characters match, so the
    /// comparison cannot be used to recover a key byte by byte.
    /// </summary>
    private static bool FixedTimeEquals(string expected, string actual)
    {
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(actual);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
