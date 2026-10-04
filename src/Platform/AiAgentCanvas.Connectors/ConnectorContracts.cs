using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Connectors;

[Flags]
public enum ConnectorCapabilities
{
    None = 0,
    Tools = 1,
    Events = 2,
    Documents = 4,
}

/// <summary>
/// What a connector is, independent of any account. <see cref="Id"/> is the stable name
/// used in configuration, connections and tool names.
/// </summary>
/// <param name="Id">Lowercase and hyphenated, such as <c>twilio-sms</c>.</param>
/// <param name="Category">A grouping for the connections page: messaging, email, crm.</param>
/// <param name="Scopes">OAuth scopes or API permissions the connector needs.</param>
/// <param name="AllowedHosts">Hosts a call may reach: exact names or <c>*.example.com</c>. Anything else is refused.</param>
/// <param name="OAuthProvider">The OAuth provider slug when <paramref name="Auth"/> is OAuth2.</param>
public sealed record ConnectorDescriptor(
    string Id,
    string DisplayName,
    string Category,
    AuthKind Auth,
    IReadOnlyList<string> Scopes,
    ConnectorCapabilities Capabilities,
    IReadOnlyList<string> AllowedHosts,
    string? OAuthProvider = null)
{
    /// <summary>The tool name prefix for the first connection of this connector.</summary>
    public string ToolPrefix => Id.Replace('-', '_');
}

public enum ConnectorState
{
    Connected,

    /// <summary>No usable credential or required setting yet.</summary>
    NotConfigured,

    /// <summary>The service refused the credential and a person must connect the account again.</summary>
    NeedsReauth,

    Error,
}

public sealed record ConnectorStatus(ConnectorState State, string Detail, DateTimeOffset CheckedAt)
{
    public static ConnectorStatus Connected(string detail = "ok") => new(ConnectorState.Connected, detail, DateTimeOffset.UtcNow);
    public static ConnectorStatus NotConfigured(string detail) => new(ConnectorState.NotConfigured, detail, DateTimeOffset.UtcNow);
    public static ConnectorStatus NeedsReauth(string detail) => new(ConnectorState.NeedsReauth, detail, DateTimeOffset.UtcNow);
    public static ConnectorStatus Error(string detail) => new(ConnectorState.Error, detail, DateTimeOffset.UtcNow);
}

/// <summary>How much damage a tool can do. Drives the default approval policy.</summary>
public enum ToolRisk
{
    /// <summary>Reads only.</summary>
    Read,

    /// <summary>Creates or changes something that can be undone or that only the owner sees, such as a draft.</summary>
    Write,

    /// <summary>Reaches another person: a text, an email, a post.</summary>
    Send,

    /// <summary>Deletes or cannot be undone.</summary>
    Destructive,
}

public sealed record WebhookRequest(string Url, IReadOnlyDictionary<string, string> Headers, ReadOnlyMemory<byte> Body);

public sealed record EventBatch(IReadOnlyList<ConnectorEvent> Events, string? NextCursor);

public sealed record SourceDocument(string Id, string Title, string Text, string? Url, DateTimeOffset ModifiedAt, bool Deleted);

/// <summary>
/// What one connection hands to its connector. It is the only route to a secret and to
/// the network: credentials come from <see cref="GetCredentialAsync"/>, and
/// <see cref="CreateHttpClient"/> returns a client that already authenticates, refuses
/// other hosts, retries, and reports telemetry.
/// </summary>
public interface IConnectionContext
{
    string ConnectionId { get; }

    string ConnectorId { get; }

    string Label { get; }

    /// <summary>
    /// Start of every tool name for this connection. The first connection of a connector
    /// uses the plain prefix and later ones add their label, so two accounts do not collide.
    /// </summary>
    string ToolPrefix { get; }

    /// <summary>Non-secret configuration of the connection.</summary>
    IReadOnlyDictionary<string, string> Settings { get; }

    ValueTask<ConnectorCredential> GetCredentialAsync(CancellationToken ct = default);

    /// <summary>For REST connectors. A connector must not build its own client.</summary>
    HttpClient CreateHttpClient();
}

/// <summary>Registered once per connector type. The host creates one connector per connection.</summary>
public interface IConnectorDefinition
{
    ConnectorDescriptor Descriptor { get; }

    IConnector Create(IConnectionContext context);

    /// <summary>
    /// The hosts this connection may reach. Defaults to the descriptor's list; a connector
    /// whose endpoint is a setting, such as an MCP server, returns that host as well.
    /// </summary>
    IReadOnlyList<string> AllowedHostsFor(IReadOnlyDictionary<string, string> settings) => Descriptor.AllowedHosts;
}

public interface IConnector : IAsyncDisposable
{
    /// <summary>Open sessions and validate settings. Throw for a setting that can never work.</summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>Reports whether the connection works. Must not throw: a failure is a status.</summary>
    Task<ConnectorStatus> CheckAsync(CancellationToken ct);
}

public interface IToolConnector : IConnector
{
    Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken ct);

    /// <summary>Raised when the tool list changes, as an MCP server may do while connected.</summary>
    event EventHandler? ToolsChanged;
}

public interface IEventSourceConnector : IConnector
{
    /// <summary>Pull mode: events since the cursor. Return an empty batch for a connector that only pushes.</summary>
    Task<EventBatch> PollAsync(string? cursor, CancellationToken ct);

    /// <summary>Push mode: checks that the request really came from the service.</summary>
    bool VerifyWebhook(WebhookRequest request, out string? failureReason);

    IReadOnlyList<ConnectorEvent> ParseWebhook(WebhookRequest request);
}

public interface IDocumentSourceConnector : IConnector
{
    IAsyncEnumerable<SourceDocument> ListChangesAsync(string? cursor, CancellationToken ct);
}
