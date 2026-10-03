using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Connectors;

public sealed class ConnectorHostNotAllowedException(string host, string connectorId)
    : InvalidOperationException($"The {connectorId} connector may not call '{host}'. Add the host to the connector's allowed hosts if the call is intended.");

public static class ConnectorHttp
{
    /// <summary>The name of the <see cref="IHttpMessageHandlerFactory"/> client for one connector type.</summary>
    public static string ClientName(string connectorId) => $"connector:{connectorId}";

    public static readonly HttpRequestOptionsKey<bool> SkipAuthKey = new("aiagentcanvas.connector.skip-auth");

    /// <summary>
    /// For a service that wants its secret somewhere other than the Authorization header.
    /// The connector then reads the credential itself and sets the header it needs.
    /// </summary>
    public static HttpRequestMessage WithoutAutoAuth(this HttpRequestMessage request)
    {
        request.Options.Set(SkipAuthKey, true);
        return request;
    }

    /// <summary>True for an exact host or a <c>*.example.com</c> pattern, which matches subdomains only.</summary>
    public static bool HostAllowed(IEnumerable<string> allowed, string host)
    {
        foreach (var pattern in allowed)
        {
            if (pattern.StartsWith("*.", StringComparison.Ordinal))
            {
                var suffix = pattern[1..];
                if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && host.Length > suffix.Length)
                    return true;
            }
            else if (string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// The outermost handler of every connector client. It enforces the host allowlist,
/// attaches the credential, refreshes an OAuth token once on a 401 and retries, and
/// reports a counter and a duration. Secrets and query strings never reach the logs.
/// </summary>
internal sealed class ConnectorHttpHandler : DelegatingHandler
{
    private readonly string _connectionId;
    private readonly string _connectorId;
    private readonly IReadOnlyList<string> _allowedHosts;
    private readonly ICredentialProvider _credentials;
    private readonly ILogger _logger;

    public ConnectorHttpHandler(
        string connectionId,
        string connectorId,
        IReadOnlyList<string> allowedHosts,
        ICredentialProvider credentials,
        ILogger logger)
    {
        _connectionId = connectionId;
        _connectorId = connectorId;
        _allowedHosts = allowedHosts;
        _credentials = credentials;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("A connector request needs an absolute address.");
        if (!uri.IsAbsoluteUri || !IsPermitted(uri))
        {
            Record("blocked", 0);
            throw new ConnectorHostNotAllowedException(uri.IsAbsoluteUri ? uri.Host : uri.ToString(), _connectorId);
        }

        var started = Stopwatch.GetTimestamp();
        var outcome = "exception";
        try
        {
            // Buffer the body once so the retry after a refresh can send it again.
            byte[]? body = null;
            HttpContentHeaders? bodyHeaders = null;
            if (request.Content is not null)
            {
                body = await request.Content.ReadAsByteArrayAsync(ct);
                bodyHeaders = request.Content.Headers;
            }

            var skipAuth = request.Options.TryGetValue(ConnectorHttp.SkipAuthKey, out var skip) && skip;
            ConnectorCredential? credential = null;
            if (!skipAuth && request.Headers.Authorization is null)
            {
                credential = await _credentials.GetAsync(_connectionId, ct);
                Attach(request, credential);
            }

            var response = await base.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.Unauthorized
                && credential is { Kind: AuthKind.OAuth2 })
            {
                // The token may have been revoked or rotated since it was issued.
                response.Dispose();
                var fresh = await _credentials.RefreshAsync(_connectionId, ct);

                var retry = CloneRequest(request, body, bodyHeaders);
                Attach(retry, fresh);
                response = await base.SendAsync(retry, ct);
            }

            outcome = Classify(response.StatusCode);
            return response;
        }
        finally
        {
            Record(outcome, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("{Connector} {Method} {Host}{Path} -> {Outcome}", _connectorId, request.Method, uri.Host, uri.AbsolutePath, outcome);
        }
    }

    private bool IsPermitted(Uri uri)
    {
        if (!ConnectorHttp.HostAllowed(_allowedHosts, uri.Host))
            return false;

        // Credentials travel in the clear over http, so only a loopback address may use it.
        return uri.Scheme == Uri.UriSchemeHttps
            || (uri.Scheme == Uri.UriSchemeHttp && (uri.IsLoopback || uri.Host == "localhost"));
    }

    private static void Attach(HttpRequestMessage request, ConnectorCredential credential)
    {
        switch (credential.Kind)
        {
            case AuthKind.None:
                break;
            case AuthKind.KeyPair:
                var pair = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credential.Value}:{credential.Secret}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", pair);
                break;
            default:
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Value);
                break;
        }
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage original, byte[]? body, HttpContentHeaders? bodyHeaders)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri) { Version = original.Version };
        foreach (var header in original.Headers)
        {
            if (!header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            if (bodyHeaders is not null)
            {
                foreach (var header in bodyHeaders)
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        foreach (var option in original.Options)
            ((IDictionary<string, object?>)clone.Options).Add(option);

        return clone;
    }

    private static string Classify(HttpStatusCode status) => (int)status switch
    {
        < 400 => "ok",
        < 500 => "client_error",
        _ => "server_error",
    };

    private void Record(string outcome, double milliseconds)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("connector", _connectorId),
            new("outcome", outcome),
        };
        AgentTelemetry.ConnectorCalls.Add(1, tags);
        if (milliseconds > 0)
            AgentTelemetry.ConnectorDuration.Record(milliseconds, tags);
    }
}
