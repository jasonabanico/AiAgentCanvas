using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Connections;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiAgentCanvas.Tests;

public sealed class ConnectionsKit : IDisposable
{
    public record CapturedRequest(string Url, string? Authorization, Dictionary<string, string> Form);

    public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"connections-{Guid.NewGuid():N}.db");
    public ConnectionsOptions Options { get; }
    public ConnectionStore Store { get; }
    public OAuthProviderRegistry Providers { get; }
    public ManualClock Clock { get; } = new();
    public List<CapturedRequest> Requests { get; } = [];
    public List<AgentNotification> Notifications { get; } = [];
    public Func<CapturedRequest, (HttpStatusCode Status, string Body)> TokenEndpoint { get; set; }
    public OAuthService OAuth { get; }
    public CredentialProvider Credentials { get; }
    public EphemeralDataProtectionProvider DataProtection { get; } = new();

    public sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StubHandler(ConnectionsKit kit) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var form = new Dictionary<string, string>();
            if (request.Content is not null)
            {
                var body = await request.Content.ReadAsStringAsync(ct);
                foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = pair.Split('=', 2);
                    form[Uri.UnescapeDataString(parts[0].Replace('+', ' '))] =
                        parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
                }
            }

            var captured = new CapturedRequest(
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                form);
            lock (kit.Requests) kit.Requests.Add(captured);

            var (status, text) = kit.TokenEndpoint(captured);
            return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class StubFactory(ConnectionsKit kit) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(kit));
    }

    private sealed class CollectingSink(ConnectionsKit kit) : INotificationSink
    {
        public Task SendAsync(AgentNotification notification, CancellationToken ct = default)
        {
            lock (kit.Notifications) kit.Notifications.Add(notification);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<AgentNotification> SubscribeAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    public ConnectionsKit(Action<ConnectionsOptions>? configure = null)
    {
        Options = new ConnectionsOptions
        {
            PublicBaseUrl = "https://agents.example.com",
            ReturnUrl = "/connections",
            RefreshSkewSeconds = 120,
        };
        configure?.Invoke(Options);

        TokenEndpoint = _ => (HttpStatusCode.OK, """{"access_token":"new-access","expires_in":3600}""");

        Store = new ConnectionStore(DbPath, new DataProtectionSecretProtector(DataProtection));
        Providers = new OAuthProviderRegistry(Options);
        var factory = new StubFactory(this);
        OAuth = new OAuthService(Store, Providers, Options, DataProtection, factory, NullLogger<OAuthService>.Instance, Clock);
        Credentials = new CredentialProvider(Store, Providers, OAuth, Options, NullLogger<CredentialProvider>.Instance, new CollectingSink(this), Clock);
    }

    public IHttpClientFactory HttpFactory => new StubFactory(this);
    public INotificationSink Sink => new CollectingSink(this);

    public void RegisterApp(string provider, string id = "client-id", string secret = "client-secret") =>
        Store.SaveOAuthApp(new OAuthApp(provider, id, secret));

    public Connection AddOAuthConnection(
        string provider = "google", TimeSpan? expiresIn = null, string access = "old-access", string? refresh = "refresh-1", string id = "conn1")
    {
        var connection = new Connection
        {
            Id = id,
            ConnectorId = "gmail",
            Label = "work",
            Auth = AuthKind.OAuth2,
            Settings = { ["oauth_provider"] = provider },
        };
        Store.Save(connection);
        Store.SaveSecret(id, new SecretPayload { AccessToken = access, RefreshToken = refresh }, Clock.Now + (expiresIn ?? TimeSpan.FromHours(1)));
        return connection;
    }

    public static string StateOf(string authorizeUrl) =>
        HttpUtility.ParseQueryString(new Uri(authorizeUrl).Query)["state"]!;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(DbPath + suffix); }
            catch (IOException) { }
        }
    }
}

public class ConnectionStoreTests : IDisposable
{
    private readonly ConnectionsKit _kit = new();

    public void Dispose() => _kit.Dispose();

    [Fact]
    public void Secrets_are_encrypted_at_rest_and_round_trip()
    {
        _kit.Store.Save(new Connection { Id = "c1", ConnectorId = "twilio-sms", Label = "main", Auth = AuthKind.KeyPair });
        _kit.Store.SaveSecret("c1", new SecretPayload { KeyId = "SKabc", KeySecret = "super-secret-value" });

        using var raw = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _kit.DbPath }.ToString());
        raw.Open();
        using var cmd = raw.CreateCommand();
        cmd.CommandText = "SELECT payload FROM connection_secrets WHERE connection_id = 'c1'";
        var stored = (string)cmd.ExecuteScalar()!;

        Assert.DoesNotContain("super-secret-value", stored);
        Assert.DoesNotContain("SKabc", stored);
        Assert.Equal("super-secret-value", _kit.Store.GetSecret("c1")!.KeySecret);
    }

    [Fact]
    public void A_tampered_secret_is_rejected_rather_than_read()
    {
        _kit.Store.Save(new Connection { Id = "c1", ConnectorId = "x", Label = "x", Auth = AuthKind.ApiKey });
        _kit.Store.SaveSecret("c1", new SecretPayload { ApiKey = "key" });

        using (var raw = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _kit.DbPath }.ToString()))
        {
            raw.Open();
            using var cmd = raw.CreateCommand();
            cmd.CommandText = "UPDATE connection_secrets SET payload = 'CfDJ8NotARealPayload' WHERE connection_id = 'c1'";
            cmd.ExecuteNonQuery();
        }

        Assert.ThrowsAny<CryptographicException>(() => _kit.Store.GetSecret("c1"));
    }

    [Fact]
    public void Deleting_a_connection_deletes_its_secret()
    {
        _kit.Store.Save(new Connection { Id = "c1", ConnectorId = "x", Label = "x", Auth = AuthKind.ApiKey });
        _kit.Store.SaveSecret("c1", new SecretPayload { ApiKey = "key" });

        Assert.True(_kit.Store.Delete("c1"));

        Assert.Null(_kit.Store.Get("c1"));
        Assert.Null(_kit.Store.GetSecret("c1"));
        Assert.False(_kit.Store.Delete("c1"));
    }

    [Fact]
    public void Connection_metadata_round_trips_and_filters_by_connector()
    {
        _kit.Store.Save(new Connection
        {
            Id = "a", ConnectorId = "gmail", Label = "work", Auth = AuthKind.OAuth2,
            Scopes = ["mail.read"], Settings = { ["from"] = "x@y.z" },
        });
        _kit.Store.Save(new Connection { Id = "b", ConnectorId = "slack", Label = "team", Auth = AuthKind.OAuth2 });

        var gmail = Assert.Single(_kit.Store.List("gmail"));
        Assert.Equal(["mail.read"], gmail.Scopes);
        Assert.Equal("x@y.z", gmail.Settings["from"]);
        Assert.Equal(2, _kit.Store.List().Count);
    }

    [Fact]
    public void The_oauth_app_secret_is_encrypted_too()
    {
        _kit.Store.SaveOAuthApp(new OAuthApp("google", "my-client", "my-client-secret"));

        using var raw = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _kit.DbPath }.ToString());
        raw.Open();
        using var cmd = raw.CreateCommand();
        cmd.CommandText = "SELECT client_secret FROM oauth_apps";
        Assert.DoesNotContain("my-client-secret", (string)cmd.ExecuteScalar()!);
        Assert.Equal("my-client-secret", _kit.Store.GetOAuthApp("google")!.ClientSecret);
    }

    [Fact]
    public void Credentials_and_payloads_do_not_print_their_secrets()
    {
        var credential = new ConnectorCredential(AuthKind.OAuth2, "token-value", "secret-value");

        Assert.DoesNotContain("token-value", credential.ToString());
        Assert.DoesNotContain("secret-value", credential.ToString());
        Assert.DoesNotContain("abc", new SecretPayload { AccessToken = "abc" }.ToString());
    }
}

public class OAuthFlowTests : IDisposable
{
    private readonly ConnectionsKit _kit = new();

    public void Dispose() => _kit.Dispose();

    [Fact]
    public void The_authorize_url_carries_pkce_scopes_defaults_and_provider_extras()
    {
        _kit.RegisterApp("google");

        var url = _kit.OAuth.Start(new OAuthStartRequest("google", "gmail", "work", ["https://www.googleapis.com/auth/gmail.modify"])).AuthorizeUrl;
        var query = HttpUtility.ParseQueryString(new Uri(url).Query);

        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", url);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("client-id", query["client_id"]);
        Assert.Equal("https://agents.example.com/api/connections/oauth/callback", query["redirect_uri"]);
        Assert.Equal("https://www.googleapis.com/auth/gmail.modify", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrEmpty(query["code_challenge"]));
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("consent", query["prompt"]);
    }

    [Fact]
    public void A_provider_default_scope_is_always_requested_and_slack_uses_commas_without_pkce()
    {
        _kit.RegisterApp("microsoft");
        _kit.RegisterApp("slack");

        var microsoft = HttpUtility.ParseQueryString(new Uri(_kit.OAuth.Start(new OAuthStartRequest("microsoft", "outlook", "x", ["Mail.Read"])).AuthorizeUrl).Query);
        Assert.Equal("offline_access Mail.Read", microsoft["scope"]);

        var slack = HttpUtility.ParseQueryString(new Uri(_kit.OAuth.Start(new OAuthStartRequest("slack", "slack", "x", ["chat:write", "channels:read"])).AuthorizeUrl).Query);
        Assert.Equal("chat:write,channels:read", slack["scope"]);
        Assert.Null(slack["code_challenge"]);
    }

    [Fact]
    public void Starting_without_an_app_or_a_public_address_or_a_known_provider_explains_what_to_fix()
    {
        Assert.Contains("no OAuth app", Assert.Throws<OAuthException>(() => _kit.OAuth.Start(new OAuthStartRequest("google", "gmail", "x"))).Message);

        Assert.Contains("nope", Assert.Throws<OAuthException>(() => _kit.OAuth.Start(new OAuthStartRequest("nope", "x", "x"))).Message);

        using var noBase = new ConnectionsKit(o => o.PublicBaseUrl = null);
        noBase.RegisterApp("google");
        Assert.Contains("PublicBaseUrl", Assert.Throws<OAuthException>(() => noBase.OAuth.Start(new OAuthStartRequest("google", "gmail", "x"))).Message);
    }

    [Fact]
    public void A_client_registered_in_configuration_works_without_the_database()
    {
        using var kit = new ConnectionsKit(o => o.OAuthApps["github"] = new OAuthAppConfig { ClientId = "cfg-id", ClientSecret = "cfg-secret" });

        var url = kit.OAuth.Start(new OAuthStartRequest("github", "github", "x")).AuthorizeUrl;

        Assert.Equal("cfg-id", HttpUtility.ParseQueryString(new Uri(url).Query)["client_id"]);
    }

    [Fact]
    public async Task Completing_the_flow_exchanges_the_code_with_the_verifier_and_stores_an_encrypted_connection()
    {
        _kit.RegisterApp("google");
        _kit.TokenEndpoint = _ => (HttpStatusCode.OK, """{"access_token":"A1","refresh_token":"R1","expires_in":3600,"scope":"mail.read mail.send"}""");

        var url = _kit.OAuth.Start(new OAuthStartRequest("google", "gmail", "work", ["mail.read"], ReturnUrl: "/settings")).AuthorizeUrl;
        var challenge = HttpUtility.ParseQueryString(new Uri(url).Query)["code_challenge"];

        var result = await _kit.OAuth.CompleteAsync("the-code", ConnectionsKit.StateOf(url), null, null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("/settings", result.ReturnUrl);

        var request = Assert.Single(_kit.Requests);
        Assert.Equal("https://oauth2.googleapis.com/token", request.Url);
        Assert.Equal("authorization_code", request.Form["grant_type"]);
        Assert.Equal("the-code", request.Form["code"]);
        Assert.Equal("client-id", request.Form["client_id"]);
        Assert.Equal("client-secret", request.Form["client_secret"]);
        Assert.Equal("https://agents.example.com/api/connections/oauth/callback", request.Form["redirect_uri"]);

        // The verifier sent to the token endpoint is the one whose challenge went to the provider.
        var verifier = request.Form["code_verifier"];
        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(challenge, expected);

        var connection = _kit.Store.Get(result.ConnectionId!)!;
        Assert.Equal(ConnectionStatus.Connected, connection.Status);
        Assert.Equal("gmail", connection.ConnectorId);
        Assert.Equal("work", connection.Label);
        Assert.Equal(AuthKind.OAuth2, connection.Auth);
        Assert.Equal(["mail.read", "mail.send"], connection.Scopes);
        Assert.Equal("google", connection.Settings["oauth_provider"]);
        Assert.Equal(_kit.Clock.Now.AddSeconds(3600), connection.ExpiresAt);

        var secret = _kit.Store.GetSecret(connection.Id)!;
        Assert.Equal("A1", secret.AccessToken);
        Assert.Equal("R1", secret.RefreshToken);
    }

    [Fact]
    public async Task A_provider_that_wants_basic_auth_gets_it_and_no_secret_in_the_body()
    {
        _kit.RegisterApp("notion", "n-id", "n-secret");
        var url = _kit.OAuth.Start(new OAuthStartRequest("notion", "notion", "x")).AuthorizeUrl;

        await _kit.OAuth.CompleteAsync("code", ConnectionsKit.StateOf(url), null, null, CancellationToken.None);

        var request = Assert.Single(_kit.Requests);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("n-id:n-secret")), request.Authorization);
        Assert.False(request.Form.ContainsKey("client_secret"));
        Assert.False(request.Form.ContainsKey("code_verifier"));
    }

    [Fact]
    public async Task Slacks_nested_user_token_and_its_ok_false_errors_are_understood()
    {
        _kit.RegisterApp("slack");
        _kit.TokenEndpoint = _ => (HttpStatusCode.OK, """{"ok":true,"authed_user":{"access_token":"xoxp-nested"}}""");
        var url = _kit.OAuth.Start(new OAuthStartRequest("slack", "slack", "team")).AuthorizeUrl;

        var ok = await _kit.OAuth.CompleteAsync("c", ConnectionsKit.StateOf(url), null, null, CancellationToken.None);

        Assert.True(ok.Success);
        Assert.Equal("xoxp-nested", _kit.Store.GetSecret(ok.ConnectionId!)!.AccessToken);

        _kit.TokenEndpoint = _ => (HttpStatusCode.OK, """{"ok":false,"error":"invalid_code"}""");
        var url2 = _kit.OAuth.Start(new OAuthStartRequest("slack", "slack", "team")).AuthorizeUrl;
        var failed = await _kit.OAuth.CompleteAsync("c", ConnectionsKit.StateOf(url2), null, null, CancellationToken.None);

        Assert.False(failed.Success);
        Assert.Contains("invalid_code", failed.Message);
    }

    [Fact]
    public async Task A_failed_exchange_creates_nothing()
    {
        _kit.RegisterApp("google");
        _kit.TokenEndpoint = _ => (HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Bad code"}""");
        var url = _kit.OAuth.Start(new OAuthStartRequest("google", "gmail", "x")).AuthorizeUrl;

        var result = await _kit.OAuth.CompleteAsync("bad", ConnectionsKit.StateOf(url), null, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Bad code", result.Message);
        Assert.Empty(_kit.Store.List());
    }

    [Fact]
    public async Task A_provider_error_a_missing_code_and_a_bad_state_each_fail_with_a_reason()
    {
        _kit.RegisterApp("google");
        var state = ConnectionsKit.StateOf(_kit.OAuth.Start(new OAuthStartRequest("google", "gmail", "x")).AuthorizeUrl);

        var denied = await _kit.OAuth.CompleteAsync(null, state, "access_denied", "The user said no", CancellationToken.None);
        Assert.Equal("The user said no", denied.Message);

        var noCode = await _kit.OAuth.CompleteAsync(null, state, null, null, CancellationToken.None);
        Assert.Contains("without a code", noCode.Message);

        var forged = await _kit.OAuth.CompleteAsync("c", "not-a-real-state", null, null, CancellationToken.None);
        Assert.Contains("did not start here", forged.Message);

        var missing = await _kit.OAuth.CompleteAsync("c", null, null, null, CancellationToken.None);
        Assert.False(missing.Success);

        Assert.Empty(_kit.Requests);
    }

    [Fact]
    public async Task A_return_url_that_leaves_this_host_is_replaced()
    {
        _kit.RegisterApp("google");

        foreach (var hostile in new[] { "https://evil.example.com", "//evil.example.com", "/\\evil.example.com", "javascript:alert(1)" })
        {
            var url = _kit.OAuth.Start(new OAuthStartRequest("google", "gmail", "x", ReturnUrl: hostile)).AuthorizeUrl;
            var result = await _kit.OAuth.CompleteAsync(null, ConnectionsKit.StateOf(url), "denied", null, CancellationToken.None);
            Assert.Equal("/", result.ReturnUrl);
        }
    }

    [Fact]
    public async Task Reconnecting_updates_the_same_connection_and_keeps_a_refresh_token_the_provider_did_not_reissue()
    {
        _kit.RegisterApp("google");
        var existing = _kit.AddOAuthConnection(refresh: "keep-me");
        _kit.Store.SetStatus(existing.Id, ConnectionStatus.NeedsReauth, "revoked");
        _kit.TokenEndpoint = _ => (HttpStatusCode.OK, """{"access_token":"fresh","expires_in":1800}""");

        var url = _kit.OAuth.Start(new OAuthStartRequest("google", "gmail", "work", ReconnectConnectionId: existing.Id)).AuthorizeUrl;
        var result = await _kit.OAuth.CompleteAsync("c", ConnectionsKit.StateOf(url), null, null, CancellationToken.None);

        Assert.Equal(existing.Id, result.ConnectionId);
        Assert.Single(_kit.Store.List());
        var connection = _kit.Store.Get(existing.Id)!;
        Assert.Equal(ConnectionStatus.Connected, connection.Status);
        Assert.Null(connection.StatusDetail);
        Assert.Equal("keep-me", _kit.Store.GetSecret(existing.Id)!.RefreshToken);
        Assert.Equal("fresh", _kit.Store.GetSecret(existing.Id)!.AccessToken);
    }

    [Fact]
    public async Task A_state_issued_under_a_different_key_is_refused()
    {
        _kit.RegisterApp("google");
        var state = ConnectionsKit.StateOf(_kit.OAuth.Start(new OAuthStartRequest("google", "gmail", "x")).AuthorizeUrl);

        // A different key ring stands in for a state that this host did not issue. The
        // ten-minute lifetime is enforced by Data Protection against the real clock.
        var other = new OAuthService(_kit.Store, _kit.Providers, _kit.Options, new EphemeralDataProtectionProvider(),
            _kit.HttpFactory, NullLogger<OAuthService>.Instance);

        var result = await other.CompleteAsync("c", state, null, null, CancellationToken.None);

        Assert.Contains("did not start here", result.Message);
    }
}

public class CredentialProviderTests : IDisposable
{
    private readonly ConnectionsKit _kit = new();

    public CredentialProviderTests() => _kit.RegisterApp("google");

    public void Dispose() => _kit.Dispose();

    [Fact]
    public async Task An_api_key_and_a_key_pair_come_back_decrypted()
    {
        _kit.Store.Save(new Connection { Id = "k1", ConnectorId = "stripe", Label = "x", Auth = AuthKind.ApiKey });
        _kit.Store.SaveSecret("k1", new SecretPayload { ApiKey = "sk_test_123" });
        _kit.Store.Save(new Connection { Id = "k2", ConnectorId = "twilio-sms", Label = "x", Auth = AuthKind.KeyPair });
        _kit.Store.SaveSecret("k2", new SecretPayload { KeyId = "SK1", KeySecret = "shh", Extras = { ["AuthToken"] = "tok" } });

        var key = await _kit.Credentials.GetAsync("k1");
        Assert.Equal((AuthKind.ApiKey, "sk_test_123"), (key.Kind, key.Value));

        var pair = await _kit.Credentials.GetAsync("k2");
        Assert.Equal(("SK1", "shh", "tok"), (pair.Value, pair.Secret, pair.Extras!["AuthToken"]));
    }

    [Fact]
    public async Task An_unknown_connection_is_not_found_and_a_flagged_one_needs_reauth()
    {
        var missing = await Assert.ThrowsAsync<CredentialException>(async () => await _kit.Credentials.GetAsync("nope"));
        Assert.Equal(CredentialFailure.NotFound, missing.Failure);

        var connection = _kit.AddOAuthConnection();
        _kit.Store.SetStatus(connection.Id, ConnectionStatus.NeedsReauth, "revoked by the user");

        var flagged = await Assert.ThrowsAsync<CredentialException>(async () => await _kit.Credentials.GetAsync(connection.Id));
        Assert.Equal(CredentialFailure.NeedsReauth, flagged.Failure);
        Assert.Contains("revoked by the user", flagged.Message);
    }

    [Fact]
    public async Task A_token_that_is_not_near_expiry_is_returned_without_calling_the_provider()
    {
        var connection = _kit.AddOAuthConnection(expiresIn: TimeSpan.FromHours(1));

        var credential = await _kit.Credentials.GetAsync(connection.Id);

        Assert.Equal("old-access", credential.Value);
        Assert.Empty(_kit.Requests);
    }

    [Fact]
    public async Task A_token_within_the_skew_is_refreshed_and_the_rotated_refresh_token_is_kept()
    {
        var connection = _kit.AddOAuthConnection(expiresIn: TimeSpan.FromSeconds(60));
        _kit.TokenEndpoint = _ => (HttpStatusCode.OK, """{"access_token":"new-access","refresh_token":"refresh-2","expires_in":3600}""");

        var credential = await _kit.Credentials.GetAsync(connection.Id);

        Assert.Equal("new-access", credential.Value);
        var request = Assert.Single(_kit.Requests);
        Assert.Equal("refresh_token", request.Form["grant_type"]);
        Assert.Equal("refresh-1", request.Form["refresh_token"]);
        Assert.Equal("client-secret", request.Form["client_secret"]);

        var stored = _kit.Store.GetSecret(connection.Id)!;
        Assert.Equal("refresh-2", stored.RefreshToken);
        Assert.Equal(_kit.Clock.Now.AddSeconds(3600), _kit.Store.Get(connection.Id)!.ExpiresAt);
    }

    [Fact]
    public async Task Concurrent_callers_share_one_refresh_so_a_rotating_token_is_not_spent_twice()
    {
        var connection = _kit.AddOAuthConnection(expiresIn: TimeSpan.FromSeconds(10));
        _kit.TokenEndpoint = _ =>
        {
            Thread.Sleep(100);
            return (HttpStatusCode.OK, """{"access_token":"shared","refresh_token":"refresh-2","expires_in":3600}""");
        };

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(async () => (await _kit.Credentials.GetAsync(connection.Id)).Value)));

        Assert.All(results, r => Assert.Equal("shared", r));
        Assert.Single(_kit.Requests);
    }

    [Fact]
    public async Task A_refused_refresh_token_flags_the_connection_notifies_once_and_stops_further_calls()
    {
        var connection = _kit.AddOAuthConnection(expiresIn: TimeSpan.FromSeconds(10));
        _kit.TokenEndpoint = _ => (HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Token has been revoked"}""");

        var first = await Assert.ThrowsAsync<CredentialException>(async () => await _kit.Credentials.GetAsync(connection.Id));
        Assert.Equal(CredentialFailure.NeedsReauth, first.Failure);

        var stored = _kit.Store.Get(connection.Id)!;
        Assert.Equal(ConnectionStatus.NeedsReauth, stored.Status);
        Assert.Contains("revoked", stored.StatusDetail);
        Assert.Equal("connection:conn1", Assert.Single(_kit.Notifications).Source);

        await Assert.ThrowsAsync<CredentialException>(async () => await _kit.Credentials.GetAsync(connection.Id));
        Assert.Single(_kit.Requests);
        Assert.Single(_kit.Notifications);
    }

    [Fact]
    public async Task A_provider_outage_does_not_break_a_token_that_still_works_but_does_once_it_has_expired()
    {
        var connection = _kit.AddOAuthConnection(expiresIn: TimeSpan.FromSeconds(60));
        _kit.TokenEndpoint = _ => (HttpStatusCode.InternalServerError, "oops");

        var stillValid = await _kit.Credentials.GetAsync(connection.Id);
        Assert.Equal("old-access", stillValid.Value);
        Assert.Equal(ConnectionStatus.Connected, _kit.Store.Get(connection.Id)!.Status);

        _kit.Clock.Now = _kit.Clock.Now.AddMinutes(5);
        var expired = await Assert.ThrowsAsync<CredentialException>(async () => await _kit.Credentials.GetAsync(connection.Id));
        Assert.Equal(CredentialFailure.Unavailable, expired.Failure);
        Assert.Equal(ConnectionStatus.Connected, _kit.Store.Get(connection.Id)!.Status);
    }

    [Fact]
    public async Task An_expired_token_with_no_refresh_token_needs_reauth()
    {
        var connection = _kit.AddOAuthConnection(provider: "github", expiresIn: TimeSpan.FromSeconds(-5), refresh: null);

        var ex = await Assert.ThrowsAsync<CredentialException>(async () => await _kit.Credentials.GetAsync(connection.Id));

        Assert.Equal(CredentialFailure.NeedsReauth, ex.Failure);
        Assert.Equal(ConnectionStatus.NeedsReauth, _kit.Store.Get(connection.Id)!.Status);
        Assert.Empty(_kit.Requests);
    }

    [Fact]
    public async Task Forcing_a_refresh_works_even_when_the_token_has_time_left()
    {
        var connection = _kit.AddOAuthConnection(expiresIn: TimeSpan.FromHours(2));

        var credential = await _kit.Credentials.RefreshAsync(connection.Id);

        Assert.Equal("new-access", credential.Value);
        Assert.Single(_kit.Requests);
    }

    [Fact]
    public async Task Marking_a_connection_for_reauth_announces_it_only_once()
    {
        var connection = _kit.AddOAuthConnection();

        await _kit.Credentials.MarkNeedsReauthAsync(connection.Id, "the API returned 401 twice");
        await _kit.Credentials.MarkNeedsReauthAsync(connection.Id, "the API returned 401 twice");

        Assert.Single(_kit.Notifications);
        Assert.Contains("Reconnect", _kit.Notifications[0].Title);
    }

    [Fact]
    public async Task The_sweep_refreshes_tokens_about_to_expire_and_leaves_the_rest()
    {
        var soon = _kit.AddOAuthConnection(expiresIn: TimeSpan.FromMinutes(3), id: "soon");
        var later = _kit.AddOAuthConnection(expiresIn: TimeSpan.FromHours(2), id: "later");
        var flagged = _kit.AddOAuthConnection(expiresIn: TimeSpan.FromMinutes(1), id: "flagged");
        _kit.Store.SetStatus(flagged.Id, ConnectionStatus.NeedsReauth, "revoked");

        var sweep = new ConnectionRefreshService(_kit.Store, _kit.Credentials, _kit.Options,
            NullLogger<ConnectionRefreshService>.Instance, _kit.Clock);
        await sweep.SweepAsync(CancellationToken.None);

        var request = Assert.Single(_kit.Requests);
        Assert.Equal("refresh-1", request.Form["refresh_token"]);
        Assert.Equal("new-access", _kit.Store.GetSecret(soon.Id)!.AccessToken);
        Assert.Equal("old-access", _kit.Store.GetSecret(later.Id)!.AccessToken);
    }
}

public class ConnectionEndpointTests : IDisposable
{
    private readonly ConnectionsKit _kit = new();
    private WebApplication? _app;

    public void Dispose()
    {
        _app?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _kit.Dispose();
    }

    private async Task<HttpClient> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(_kit.Store);
        builder.Services.AddSingleton(_kit.Providers);
        builder.Services.AddSingleton(_kit.OAuth);
        _app = builder.Build();
        _app.MapConnectionEndpoints();
        _app.MapConnectionCallback();
        await _app.StartAsync();

        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };
    }

    [Fact]
    public async Task Creating_a_key_connection_never_echoes_the_secret_and_listing_stays_clean()
    {
        var client = await StartAsync();

        var created = await client.PostAsJsonAsync("/api/connections/key", new
        {
            connectorId = "twilio-sms",
            label = "main",
            kind = "KeyPair",
            keyId = "SKabc",
            keySecret = "do-not-leak",
            extras = new Dictionary<string, string> { ["AuthToken"] = "also-secret" },
            settings = new Dictionary<string, string> { ["FromNumber"] = "+61400000000" },
        });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadAsStringAsync();
        var list = await client.GetStringAsync("/api/connections");

        foreach (var text in new[] { body, list })
        {
            Assert.DoesNotContain("do-not-leak", text);
            Assert.DoesNotContain("also-secret", text);
            Assert.DoesNotContain("SKabc", text);
            Assert.Contains("twilio-sms", text);
        }
        Assert.Contains("+61400000000", list);
    }

    [Fact]
    public async Task A_key_connection_with_missing_fields_or_a_bad_kind_is_rejected()
    {
        var client = await StartAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/connections/key",
            new { connectorId = "x", kind = "OAuth2" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/connections/key",
            new { connectorId = "x", kind = "ApiKey" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/connections/key",
            new { connectorId = "x", kind = "KeyPair", keyId = "only-id" })).StatusCode);
        Assert.Empty(_kit.Store.List());
    }

    [Fact]
    public async Task A_connection_can_be_deleted_with_its_secret()
    {
        var client = await StartAsync();
        var created = await (await client.PostAsJsonAsync("/api/connections/key",
            new { connectorId = "stripe", kind = "ApiKey", apiKey = "sk_x" })).Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var id = created!["id"]!.ToString()!;

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/connections/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/connections/{id}")).StatusCode);
        Assert.Null(_kit.Store.GetSecret(id));
    }

    [Fact]
    public async Task The_full_oauth_round_trip_works_through_the_endpoints()
    {
        var client = await StartAsync();

        Assert.Contains("\"appConfigured\":false", await client.GetStringAsync("/api/connections/oauth/providers"));

        var noApp = await client.PostAsJsonAsync("/api/connections/oauth/google/start", new { connectorId = "gmail" });
        Assert.Equal(HttpStatusCode.BadRequest, noApp.StatusCode);

        var saved = await client.PutAsJsonAsync("/api/connections/oauth/apps/google", new { clientId = "cid", clientSecret = "csecret" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.DoesNotContain("csecret", await saved.Content.ReadAsStringAsync());

        var started = await client.PostAsJsonAsync("/api/connections/oauth/google/start", new { connectorId = "gmail", label = "work", returnUrl = "/done" });
        var authorizeUrl = (await started.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["authorizeUrl"];

        _kit.TokenEndpoint = _ => (HttpStatusCode.OK, """{"access_token":"A","refresh_token":"R","expires_in":3600}""");
        var state = Uri.EscapeDataString(ConnectionsKit.StateOf(authorizeUrl));
        var callback = await client.GetAsync($"{OAuthService.CallbackPath}?code=abc&state={state}");

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        var location = callback.Headers.Location!.ToString();
        Assert.StartsWith("/done?connection=ok&id=", location);
        Assert.Single(_kit.Store.List());
    }

    [Fact]
    public async Task A_callback_with_a_bad_state_redirects_with_an_error_and_makes_no_connection()
    {
        var client = await StartAsync();

        var callback = await client.GetAsync($"{OAuthService.CallbackPath}?code=abc&state=forged");

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Contains("connection=error", callback.Headers.Location!.ToString());
        Assert.StartsWith("/connections", callback.Headers.Location.ToString());
        Assert.Empty(_kit.Store.List());
    }
}
