using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Connections;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Connectors;

internal sealed class ConnectionContext : IConnectionContext
{
    private readonly ICredentialProvider _credentials;
    private readonly IHttpMessageHandlerFactory _handlers;
    private readonly IReadOnlyList<string> _allowedHosts;
    private readonly TimeSpan _timeout;
    private readonly ILogger _logger;

    public ConnectionContext(
        Connection connection,
        ConnectorDescriptor descriptor,
        string toolPrefix,
        IReadOnlyList<string> allowedHosts,
        ICredentialProvider credentials,
        IHttpMessageHandlerFactory handlers,
        TimeSpan timeout,
        ILogger logger)
    {
        ConnectionId = connection.Id;
        ConnectorId = descriptor.Id;
        Label = connection.Label;
        ToolPrefix = toolPrefix;
        Settings = new Dictionary<string, string>(connection.Settings, StringComparer.OrdinalIgnoreCase);
        _credentials = credentials;
        _handlers = handlers;
        _allowedHosts = allowedHosts;
        _timeout = timeout;
        _logger = logger;
    }

    public string ConnectionId { get; }
    public string ConnectorId { get; }
    public string Label { get; }
    public string ToolPrefix { get; }
    public IReadOnlyDictionary<string, string> Settings { get; }

    public ValueTask<ConnectorCredential> GetCredentialAsync(CancellationToken ct = default) =>
        _credentials.GetAsync(ConnectionId, ct);

    public HttpClient CreateHttpClient()
    {
        var pipeline = _handlers.CreateHandler(ConnectorHttp.ClientName(ConnectorId));
        var handler = new ConnectorHttpHandler(ConnectionId, ConnectorId, _allowedHosts, _credentials, _logger)
        {
            InnerHandler = pipeline,
        };

        // The factory owns the inner pipeline's lifetime, so this client must not dispose it.
        return new HttpClient(handler, disposeHandler: false) { Timeout = _timeout };
    }
}
