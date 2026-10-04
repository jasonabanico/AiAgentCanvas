using System.Net;
using System.Text.RegularExpressions;
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace AiAgentCanvas.Connectors.Mcp;

/// <summary>
/// Connects an account to a remote MCP server and offers the server's tools to the
/// agents. The connection's HTTP client carries the traffic, so the credential comes from
/// the credential store, a refreshed token is picked up, calls reach only the server's
/// own host, and nothing about the account passes through the model.
///
/// Settings: <c>endpoint</c> (the server's https address, required), <c>include</c> (a
/// comma-separated list of tool names to expose; empty exposes all), and
/// <c>risk.&lt;tool&gt;</c> to override a tool's risk (Read, Write, Send or Destructive).
/// </summary>
public sealed class McpConnectorDefinition : IConnectorDefinition
{
    public McpConnectorDefinition(ConnectorDescriptor descriptor, IReadOnlyDictionary<string, string>? riskOverrides = null)
    {
        Descriptor = descriptor;
        RiskOverrides = riskOverrides ?? new Dictionary<string, string>();
    }

    public ConnectorDescriptor Descriptor { get; }

    /// <summary>Risks the definition fixes for tools it knows about, by original tool name.</summary>
    public IReadOnlyDictionary<string, string> RiskOverrides { get; }

    /// <summary>The generic definition, for any server that accepts a bearer token or an API key.</summary>
    public static McpConnectorDefinition Generic { get; } = new(new ConnectorDescriptor(
        "mcp", "MCP server", "integration", AuthKind.ApiKey, [],
        ConnectorCapabilities.Tools, []));

    /// <summary>
    /// Gmail through an MCP server that fronts it. The default scopes read mail and
    /// create drafts. Add <c>gmail.send</c> when connecting only if agents should send.
    /// </summary>
    public static McpConnectorDefinition Gmail { get; } = new(new ConnectorDescriptor(
        "gmail", "Gmail", "email", AuthKind.OAuth2,
        ["https://www.googleapis.com/auth/gmail.readonly", "https://www.googleapis.com/auth/gmail.compose"],
        ConnectorCapabilities.Tools, [], OAuthProvider: "google"));

    public IConnector Create(IConnectionContext context) => new McpConnector(Descriptor, context, RiskOverrides);

    public IReadOnlyList<string> AllowedHostsFor(IReadOnlyDictionary<string, string> settings) =>
        settings.TryGetValue("endpoint", out var endpoint) && Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            ? [uri.Host]
            : [];
}

public static class McpConnectorServiceExtensions
{
    public static IServiceCollection AddMcpConnector(this IServiceCollection services) =>
        services.AddConnector(McpConnectorDefinition.Generic);

    public static IServiceCollection AddGmailMcpConnector(this IServiceCollection services) =>
        services.AddConnector(McpConnectorDefinition.Gmail);
}

public sealed partial class McpConnector : IToolConnector
{
    private static readonly string[] SendWords = ["send", "post", "publish", "reply", "forward", "broadcast", "tweet", "dm"];
    private static readonly string[] DestructiveWords = ["delete", "remove", "trash", "purge", "drop", "destroy", "erase", "cancel", "revoke"];
    private static readonly string[] ReadWords = ["get", "list", "search", "read", "fetch", "find", "describe", "show", "view", "query", "count", "lookup"];

    private readonly ConnectorDescriptor _descriptor;
    private readonly IConnectionContext _context;
    private readonly IReadOnlyDictionary<string, string> _definitionRisks;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private HttpClient? _http;
    private Uri? _endpoint;
    private McpClient? _client;
    private IAsyncDisposable? _toolsChangedRegistration;
    private HashSet<string>? _include;
    private bool _disposed;

    public McpConnector(ConnectorDescriptor descriptor, IConnectionContext context, IReadOnlyDictionary<string, string> definitionRisks)
    {
        _descriptor = descriptor;
        _context = context;
        _definitionRisks = definitionRisks;
    }

    public event EventHandler? ToolsChanged;

    public async Task StartAsync(CancellationToken ct)
    {
        var endpoint = _context.Settings.GetValueOrDefault("endpoint");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
        {
            throw new InvalidOperationException("Setting 'endpoint' must be the MCP server's https address.");
        }
        _endpoint = uri;

        if (_context.Settings.TryGetValue("include", out var include) && !string.IsNullOrWhiteSpace(include))
        {
            _include = include.Split([',', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        // Every risk override must name a real risk. A typo that silently fell back to the
        // default would hide the intent to require approval.
        foreach (var (key, value) in _context.Settings.Where(p => p.Key.StartsWith("risk.", StringComparison.OrdinalIgnoreCase)))
        {
            if (!Enum.TryParse<ToolRisk>(value, true, out _))
                throw new InvalidOperationException($"Setting '{key}' must be Read, Write, Send or Destructive.");
        }

        _http = _context.CreateHttpClient();
        await ConnectAsync(ct);
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        var options = new HttpClientTransportOptions
        {
            Endpoint = _endpoint!,
            Name = $"{_descriptor.Id}:{_context.ConnectionId}",
            ConnectionTimeout = TimeSpan.FromSeconds(30),
        };

        // The client belongs to the connection, so the transport must not dispose it.
        var transport = new HttpClientTransport(options, _http!, loggerFactory: null, ownsHttpClient: false);
        _client = await McpClient.CreateAsync(transport, cancellationToken: ct);

        _toolsChangedRegistration = _client.RegisterNotificationHandler(
            "notifications/tools/list_changed",
            (_, _) =>
            {
                ToolsChanged?.Invoke(this, EventArgs.Empty);
                return ValueTask.CompletedTask;
            });
    }

    public async Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken ct)
    {
        var client = _client ?? throw new InvalidOperationException("The MCP connection is not open.");
        var remote = await client.ListToolsAsync(cancellationToken: ct);

        var tools = new List<AITool>();
        var used = new HashSet<string>();
        foreach (var tool in remote)
        {
            if (_include is not null && !_include.Contains(tool.Name))
                continue;

            var verb = Normalize(tool.Name);
            var candidate = verb;
            for (var n = 2; !used.Add(candidate); n++)
                candidate = $"{verb}_{n}";

            tools.Add(ConnectorTools.Wrap(_context, tool, candidate, ClassifyRisk(tool)));
        }
        return tools;
    }

    /// <summary>
    /// Decides how much approval a server's tool needs. A person's setting wins, then the
    /// definition's, then the tool's name: a name that sends or deletes is treated as such
    /// whatever the server claims. Only after that are the server's own hints trusted, and
    /// a server cannot talk a tool down from Send or Destructive.
    /// </summary>
    internal ToolRisk ClassifyRisk(McpClientTool tool)
    {
        if (_context.Settings.TryGetValue($"risk.{tool.Name}", out var set) && Enum.TryParse<ToolRisk>(set, true, out var configured))
            return configured;
        if (_definitionRisks.TryGetValue(tool.Name, out var known) && Enum.TryParse<ToolRisk>(known, true, out var fixedRisk))
            return fixedRisk;

        var words = Words(tool.Name);
        if (words.Any(w => DestructiveWords.Contains(w)))
            return ToolRisk.Destructive;
        if (words.Any(w => SendWords.Contains(w)))
            return ToolRisk.Send;

        var hints = tool.ProtocolTool.Annotations;
        if (hints?.DestructiveHint == true)
            return ToolRisk.Destructive;
        if (hints?.ReadOnlyHint == true || words.Any(w => ReadWords.Contains(w)))
            return ToolRisk.Read;

        return ToolRisk.Write;
    }

    private static string[] Words(string name) =>
        SplitWords().Split(Regex.Replace(name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant())
            .Where(w => w.Length > 0).ToArray();

    internal static string Normalize(string name)
    {
        var verb = SplitWords().Replace(Regex.Replace(name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant(), "_").Trim('_');
        if (verb.Length == 0 || !char.IsLetter(verb[0]))
            verb = "t_" + verb;
        return verb.Length > 40 ? verb[..40].TrimEnd('_') : verb;
    }

    public async Task<ConnectorStatus> CheckAsync(CancellationToken ct)
    {
        if (_http is null)
            return ConnectorStatus.NotConfigured("The connector has not started.");

        await _gate.WaitAsync(ct);
        try
        {
            if (_client is not null)
            {
                try
                {
                    await _client.PingAsync(cancellationToken: ct);
                    return ConnectorStatus.Connected();
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    if (IsAuthFailure(ex))
                        return ConnectorStatus.NeedsReauth("The MCP server refused the stored credentials.");
                }

                await CloseClientAsync();
            }

            // The session is gone, as it is when a server restarts. Reconnect, then tell
            // the host so it fetches tools bound to the new session.
            try
            {
                await ConnectAsync(ct);
                ToolsChanged?.Invoke(this, EventArgs.Empty);
                return ConnectorStatus.Connected("reconnected");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return IsAuthFailure(ex)
                    ? ConnectorStatus.NeedsReauth("The MCP server refused the stored credentials.")
                    : ConnectorStatus.Error($"Could not reach the MCP server: {ex.Message}");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsAuthFailure(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            if (e is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden })
                return true;
            if (e is CredentialException { Failure: CredentialFailure.NeedsReauth })
                return true;
            if (e.InnerException is null)
                break;
        }
        return false;
    }

    private async Task CloseClientAsync()
    {
        var client = _client;
        var registration = _toolsChangedRegistration;
        _client = null;
        _toolsChangedRegistration = null;

        try
        {
            if (registration is not null)
                await registration.DisposeAsync();
            if (client is not null)
                await client.DisposeAsync();
        }
        catch
        {
            // The session was already broken.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        await CloseClientAsync();
        _http?.Dispose();
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SplitWords();
}
