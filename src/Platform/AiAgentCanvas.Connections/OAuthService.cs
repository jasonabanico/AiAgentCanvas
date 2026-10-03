using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using AiAgentCanvas.Abstractions;

namespace AiAgentCanvas.Connections;

public sealed record OAuthStartRequest(
    string Provider,
    string ConnectorId,
    string Label,
    IReadOnlyList<string>? Scopes = null,
    string? ReturnUrl = null,
    string? ReconnectConnectionId = null);

public sealed record OAuthStartResult(string AuthorizeUrl);

public sealed record OAuthCompleteResult(bool Success, string? ConnectionId, string ReturnUrl, string Message);

/// <summary>A problem the caller can fix, such as a missing app registration. The message is safe to show.</summary>
public sealed class OAuthException(string message) : Exception(message);

/// <summary>
/// The OAuth 2.0 authorization code flow with PKCE. The pending authorization is carried
/// in the <c>state</c> value itself, encrypted, authenticated and valid for ten minutes,
/// so no server-side session is needed and a callback cannot be forged or replayed late.
/// </summary>
public sealed class OAuthService
{
    internal static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);
    public const string HttpClientName = "connections-oauth";
    public const string CallbackPath = "/api/connections/oauth/callback";

    private readonly ConnectionStore _store;
    private readonly OAuthProviderRegistry _providers;
    private readonly ConnectionsOptions _options;
    private readonly ITimeLimitedDataProtector _stateProtector;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<OAuthService> _logger;
    private readonly TimeProvider _time;

    public OAuthService(
        ConnectionStore store,
        OAuthProviderRegistry providers,
        ConnectionsOptions options,
        IDataProtectionProvider dataProtection,
        IHttpClientFactory http,
        ILogger<OAuthService> logger,
        TimeProvider? time = null)
    {
        _store = store;
        _providers = providers;
        _options = options;
        _stateProtector = dataProtection.CreateProtector("AiAgentCanvas.Connections.OAuthState.v1").ToTimeLimitedDataProtector();
        _http = http;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public OAuthStartResult Start(OAuthStartRequest request)
    {
        var provider = _providers.Get(request.Provider)
            ?? throw new OAuthException($"Unknown provider '{request.Provider}'. Available: {string.Join(", ", _providers.List().Select(p => p.Slug))}.");

        var app = ResolveApp(provider.Slug)
            ?? throw new OAuthException(
                $"{provider.Name} has no OAuth app yet. Register one with the provider, then save its client id and secret.");

        if (request.ReconnectConnectionId is { } reconnect && _store.Get(reconnect) is null)
            throw new OAuthException($"There is no connection '{reconnect}' to reconnect.");

        var redirectUri = RedirectUri();
        var verifier = provider.Pkce ? NewVerifier() : null;

        var state = _stateProtector.Protect(
            JsonSerializer.Serialize(new StatePayload(
                provider.Slug, verifier, request.ConnectorId, request.Label,
                SafeReturnUrl(request.ReturnUrl), request.Scopes?.ToList(), request.ReconnectConnectionId,
                Convert.ToHexString(RandomNumberGenerator.GetBytes(8)))),
            StateLifetime);

        var scopes = provider.DefaultScopes.Concat(request.Scopes ?? []).Distinct(StringComparer.Ordinal).ToList();

        var query = new List<KeyValuePair<string, string>>
        {
            new("response_type", "code"),
            new("client_id", app.ClientId),
            new("redirect_uri", redirectUri),
            new("state", state),
        };
        if (scopes.Count > 0)
            query.Add(new("scope", string.Join(provider.ScopeSeparator, scopes)));
        if (verifier is not null)
        {
            query.Add(new("code_challenge", Challenge(verifier)));
            query.Add(new("code_challenge_method", "S256"));
        }
        foreach (var (key, value) in provider.ExtraAuthorizeParams ?? new Dictionary<string, string>())
            query.Add(new(key, value));

        var separator = provider.AuthorizeUrl.Contains('?') ? "&" : "?";
        var url = provider.AuthorizeUrl + separator
            + string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        return new OAuthStartResult(url);
    }

    public async Task<OAuthCompleteResult> CompleteAsync(
        string? code, string? state, string? error, string? errorDescription, CancellationToken ct)
    {
        var payload = TryReadState(state);
        if (payload is null)
            return Failed(SafeReturnUrl(null), "that authorization did not start here, or it expired");

        var returnUrl = SafeReturnUrl(payload.ReturnUrl);

        if (!string.IsNullOrEmpty(error))
            return Failed(returnUrl, errorDescription ?? error);
        if (string.IsNullOrEmpty(code))
            return Failed(returnUrl, "the provider came back without a code");

        var provider = _providers.Get(payload.Provider);
        var app = provider is null ? null : ResolveApp(provider.Slug);
        if (provider is null || app is null)
            return Failed(returnUrl, "the provider's app registration is no longer available");

        var fields = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri(),
        };
        foreach (var (key, value) in OAuthRequests.ClientFields(provider, app))
            fields[key] = value;
        if (payload.Verifier is not null)
            fields["code_verifier"] = payload.Verifier;

        var token = await PostTokenAsync(provider, app, fields, ct);
        if (!token.IsSuccess)
        {
            _logger.LogWarning("Token exchange with {Provider} failed: {Error}", provider.Slug, token.Error);
            return Failed(returnUrl, token.ErrorDescription ?? token.Error ?? "the token exchange failed");
        }

        var connection = payload.ReconnectId is { } id ? _store.Get(id) : null;
        var existingSecret = connection is null ? null : _store.GetSecret(connection.Id);

        connection ??= new Connection
        {
            ConnectorId = payload.ConnectorId,
            Label = string.IsNullOrWhiteSpace(payload.Label) ? payload.ConnectorId : payload.Label,
            Auth = AuthKind.OAuth2,
        };

        connection.Auth = AuthKind.OAuth2;
        connection.Status = ConnectionStatus.Connected;
        connection.StatusDetail = null;
        connection.Scopes = token.Scopes.Count > 0 ? token.Scopes.ToList() : payload.Scopes ?? [];
        connection.Settings["oauth_provider"] = provider.Slug;

        var expiresAt = token.ExpiresInSeconds is { } seconds ? _time.GetUtcNow().AddSeconds(seconds) : (DateTimeOffset?)null;

        _store.Save(connection);
        _store.SaveSecret(connection.Id, new SecretPayload
        {
            AccessToken = token.AccessToken,
            // A reconnect that is not issued a new refresh token keeps the one already held.
            RefreshToken = token.RefreshToken ?? existingSecret?.RefreshToken,
        }, expiresAt);

        _logger.LogInformation("Connected {Provider} account as connection {ConnectionId}", provider.Slug, connection.Id);
        return new OAuthCompleteResult(true, connection.Id, returnUrl, $"{provider.Name} connected");
    }

    internal async Task<OAuthTokenResponse> PostTokenAsync(
        OAuthProviderDefinition provider, OAuthApp app, Dictionary<string, string> fields, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, provider.TokenUrl) { Content = new FormUrlEncodedContent(fields) };
            request.Headers.Accept.ParseAdd("application/json");
            if (provider.ClientAuth == OAuthClientAuth.Basic)
                request.Headers.TryAddWithoutValidation("Authorization", OAuthRequests.BasicAuthorization(app.ClientId, app.ClientSecret));

            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var parsed = OAuthTokenResponse.Parse(body);

            // A non-success status with no recognizable error still has to read as a failure.
            return !response.IsSuccessStatusCode && parsed.Error is null
                ? new OAuthTokenResponse { Error = $"http_{(int)response.StatusCode}", ErrorDescription = $"the token endpoint answered {(int)response.StatusCode}" }
                : parsed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new OAuthTokenResponse { Error = "unavailable", ErrorDescription = "the token endpoint could not be reached" };
        }
    }

    internal OAuthApp? ResolveApp(string provider)
    {
        if (_options.OAuthApps.TryGetValue(provider, out var configured)
            && !string.IsNullOrWhiteSpace(configured.ClientId) && !string.IsNullOrWhiteSpace(configured.ClientSecret))
        {
            return new OAuthApp(provider, configured.ClientId, configured.ClientSecret);
        }

        return _store.GetOAuthApp(provider);
    }

    internal string RedirectUri()
    {
        if (string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            throw new OAuthException(
                "Connections:PublicBaseUrl is not set. It is the address the browser reaches this host at, "
                + "and the OAuth redirect address is built from it.");
        }

        return _options.PublicBaseUrl.TrimEnd('/') + CallbackPath;
    }

    /// <summary>Only a path on this host is accepted, so the callback cannot be turned into an open redirect.</summary>
    internal string SafeReturnUrl(string? requested)
    {
        var candidate = string.IsNullOrWhiteSpace(requested) ? _options.ReturnUrl : requested;
        var ok = candidate.StartsWith('/')
            && !candidate.StartsWith("//", StringComparison.Ordinal)
            && !candidate.StartsWith("/\\", StringComparison.Ordinal)
            && !candidate.Contains('\r') && !candidate.Contains('\n');
        return ok ? candidate : "/";
    }

    private StatePayload? TryReadState(string? state)
    {
        if (string.IsNullOrEmpty(state))
            return null;

        try
        {
            return JsonSerializer.Deserialize<StatePayload>(_stateProtector.Unprotect(state));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            return null;
        }
    }

    private static OAuthCompleteResult Failed(string returnUrl, string message) =>
        new(false, null, returnUrl, message);

    private static string NewVerifier()
    {
        const string unreserved = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
        var bytes = RandomNumberGenerator.GetBytes(64);
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
            chars[i] = unreserved[bytes[i] % unreserved.Length];
        return new string(chars);
    }

    internal static string Challenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record StatePayload(
        string Provider,
        string? Verifier,
        string ConnectorId,
        string Label,
        string ReturnUrl,
        List<string>? Scopes,
        string? ReconnectId,
        string Nonce);
}
