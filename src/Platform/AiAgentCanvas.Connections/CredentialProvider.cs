using System.Collections.Concurrent;
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Connections;

/// <summary>
/// Hands out credentials that work now. API keys are decrypted and returned. An OAuth
/// access token close to expiry is refreshed first, by one caller at a time per
/// connection, because providers that rotate refresh tokens invalidate the old one the
/// moment a refresh succeeds and a second concurrent refresh would then fail.
/// </summary>
public sealed class CredentialProvider : ICredentialProvider
{
    private readonly ConnectionStore _store;
    private readonly OAuthProviderRegistry _providers;
    private readonly OAuthService _oauth;
    private readonly ConnectionsOptions _options;
    private readonly ILogger<CredentialProvider> _logger;
    private readonly INotificationSink? _sink;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public CredentialProvider(
        ConnectionStore store,
        OAuthProviderRegistry providers,
        OAuthService oauth,
        ConnectionsOptions options,
        ILogger<CredentialProvider> logger,
        INotificationSink? sink = null,
        TimeProvider? time = null)
    {
        _store = store;
        _providers = providers;
        _oauth = oauth;
        _options = options;
        _logger = logger;
        _sink = sink;
        _time = time ?? TimeProvider.System;
    }

    public async ValueTask<ConnectorCredential> GetAsync(string connectionId, CancellationToken ct = default)
    {
        var connection = Require(connectionId);

        if (connection.Status == ConnectionStatus.NeedsReauth)
            throw NeedsReauth(connection);

        var secret = _store.GetSecret(connectionId)
            ?? throw new CredentialException(CredentialFailure.Unavailable, $"Connection '{connectionId}' has no stored secret.");

        switch (connection.Auth)
        {
            case AuthKind.None:
                return new ConnectorCredential(AuthKind.None, string.Empty);

            case AuthKind.ApiKey:
                return new ConnectorCredential(AuthKind.ApiKey, Require(secret.ApiKey, connectionId, "an API key"));

            case AuthKind.KeyPair:
                return new ConnectorCredential(
                    AuthKind.KeyPair,
                    Require(secret.KeyId, connectionId, "a key id"),
                    Require(secret.KeySecret, connectionId, "a key secret"),
                    secret.Extras.Count == 0 ? null : new Dictionary<string, string>(secret.Extras));

            default:
                return IsExpiring(connection)
                    ? await RefreshCoreAsync(connectionId, force: false, ct)
                    : OAuthCredential(secret);
        }
    }

    public async ValueTask<ConnectorCredential> RefreshAsync(string connectionId, CancellationToken ct = default)
    {
        var connection = Require(connectionId);
        return connection.Auth == AuthKind.OAuth2
            ? await RefreshCoreAsync(connectionId, force: true, ct)
            : await GetAsync(connectionId, ct);
    }

    public async Task MarkNeedsReauthAsync(string connectionId, string reason, CancellationToken ct = default)
    {
        var connection = _store.Get(connectionId);
        if (connection is null || connection.Status == ConnectionStatus.NeedsReauth)
            return;

        _store.SetStatus(connectionId, ConnectionStatus.NeedsReauth, reason);
        _logger.LogWarning("Connection {ConnectionId} ({Connector}) needs to be connected again: {Reason}",
            connectionId, connection.ConnectorId, reason);

        if (_sink is null)
            return;

        try
        {
            await _sink.SendAsync(new AgentNotification
            {
                Title = $"Reconnect {connection.Label}",
                Body = $"The {connection.ConnectorId} connection '{connection.Label}' was refused: {reason} Connect the account again to resume.",
                Source = $"connection:{connectionId}",
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send the reconnect notification for {ConnectionId}", connectionId);
        }
    }

    internal bool IsExpiring(Connection connection) =>
        connection.ExpiresAt is { } expires
        && expires <= _time.GetUtcNow().AddSeconds(Math.Max(0, _options.RefreshSkewSeconds));

    private async ValueTask<ConnectorCredential> RefreshCoreAsync(string connectionId, bool force, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Another caller may have refreshed while this one waited.
            var connection = Require(connectionId);
            if (connection.Status == ConnectionStatus.NeedsReauth)
                throw NeedsReauth(connection);

            var secret = _store.GetSecret(connectionId)
                ?? throw new CredentialException(CredentialFailure.Unavailable, $"Connection '{connectionId}' has no stored secret.");

            if (!force && !IsExpiring(connection))
                return OAuthCredential(secret);

            var expired = connection.ExpiresAt is { } e && e <= _time.GetUtcNow();

            if (string.IsNullOrEmpty(secret.RefreshToken))
            {
                if (!expired)
                    return OAuthCredential(secret);

                const string reason = "the access token expired and there is no refresh token.";
                await MarkNeedsReauthAsync(connectionId, reason, ct);
                throw new CredentialException(CredentialFailure.NeedsReauth, $"Connection '{connectionId}': {reason}");
            }

            var providerSlug = connection.Settings.GetValueOrDefault("oauth_provider");
            var provider = providerSlug is null ? null : _providers.Get(providerSlug);
            var app = provider is null ? null : _oauth.ResolveApp(provider.Slug);
            if (provider is null || app is null)
            {
                throw new CredentialException(CredentialFailure.Unavailable,
                    $"Connection '{connectionId}' cannot be refreshed because its provider app registration is missing.");
            }

            var fields = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = secret.RefreshToken,
            };
            foreach (var (key, value) in OAuthRequests.ClientFields(provider, app))
                fields[key] = value;

            var token = await _oauth.PostTokenAsync(provider, app, fields, ct);

            if (token.IsSuccess)
            {
                secret.AccessToken = token.AccessToken;
                // Providers that rotate refresh tokens return a new one. Keep the old one otherwise.
                if (!string.IsNullOrEmpty(token.RefreshToken))
                    secret.RefreshToken = token.RefreshToken;

                var expiresAt = token.ExpiresInSeconds is { } seconds ? _time.GetUtcNow().AddSeconds(seconds) : (DateTimeOffset?)null;
                _store.SaveSecret(connectionId, secret, expiresAt);
                return OAuthCredential(secret);
            }

            if (token.IsInvalidGrant)
            {
                var reason = token.ErrorDescription ?? "the provider no longer accepts the stored authorization.";
                await MarkNeedsReauthAsync(connectionId, reason, ct);
                throw new CredentialException(CredentialFailure.NeedsReauth, $"Connection '{connectionId}': {reason}");
            }

            // A failed refresh does not matter yet if the current token still works.
            if (!expired)
            {
                _logger.LogWarning("Refreshing connection {ConnectionId} failed ({Error}); the current token is still valid",
                    connectionId, token.Error);
                return OAuthCredential(secret);
            }

            throw new CredentialException(CredentialFailure.Unavailable,
                $"Connection '{connectionId}' could not be refreshed: {token.ErrorDescription ?? token.Error}.");
        }
        finally
        {
            gate.Release();
        }
    }

    private static ConnectorCredential OAuthCredential(SecretPayload secret) =>
        new(AuthKind.OAuth2, secret.AccessToken ?? throw new CredentialException(
            CredentialFailure.Unavailable, "The connection has no access token."));

    private Connection Require(string connectionId) =>
        _store.Get(connectionId)
            ?? throw new CredentialException(CredentialFailure.NotFound, $"No connection with id '{connectionId}'.");

    private static string Require(string? value, string connectionId, string what) =>
        string.IsNullOrEmpty(value)
            ? throw new CredentialException(CredentialFailure.Unavailable, $"Connection '{connectionId}' is missing {what}.")
            : value;

    private static CredentialException NeedsReauth(Connection connection) =>
        new(CredentialFailure.NeedsReauth,
            $"Connection '{connection.Id}' needs to be connected again: {connection.StatusDetail ?? "the provider refused the stored authorization"}.");
}
