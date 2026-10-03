#pragma warning disable MEAI001

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Connections;
using AiAgentCanvas.Orchestration.Services;
using AiAgentCanvas.Orchestration.Skills;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Connectors;

public enum WebhookOutcome
{
    Accepted,
    NotFound,
    Unauthorized,
    BadRequest,

    /// <summary>A subscriber's backlog is full. The sender should retry later.</summary>
    Busy,
}

/// <summary>What the connections page shows for one connection.</summary>
public sealed record ConnectorRuntimeInfo(
    string ConnectionId,
    string ConnectorId,
    string Label,
    string ToolPrefix,
    ConnectorState State,
    string Detail,
    int ToolCount,
    ConnectorCapabilities Capabilities,
    DateTimeOffset? CheckedAt);

/// <summary>
/// Runs one connector per stored connection. It starts connectors, registers their tools
/// with the agents (wrapped in governance, tracing and, for tools that send or delete,
/// approval), checks that each connection still works, polls event sources, and takes
/// webhook deliveries. A connector that fails to start does not stop the others.
/// </summary>
public sealed class ConnectorHost : BackgroundService
{
    private readonly ConnectorRegistry _registry;
    private readonly ConnectionStore _store;
    private readonly ICredentialProvider _credentials;
    private readonly IHttpMessageHandlerFactory _handlers;
    private readonly DynamicToolRegistry _tools;
    private readonly IServiceProvider _services;
    private readonly ConnectorOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ConnectorHost> _logger;
    private readonly IConnectorEventSink? _sink;
    private readonly ICursorStore? _cursors;
    private readonly TimeProvider _time;

    private readonly ConcurrentDictionary<string, Running> _running = new();
    private readonly ConcurrentDictionary<string, Failure> _failures = new();
    private readonly ConcurrentDictionary<string, ConnectorRuntimeInfo> _info = new();
    private readonly SemaphoreSlim _reconcileGate = new(1, 1);

    public ConnectorHost(
        ConnectorRegistry registry,
        ConnectionStore store,
        ICredentialProvider credentials,
        IHttpMessageHandlerFactory handlers,
        DynamicToolRegistry tools,
        IServiceProvider services,
        ConnectorOptions options,
        ILoggerFactory loggerFactory,
        IConnectorEventSink? sink = null,
        ICursorStore? cursors = null,
        TimeProvider? time = null)
    {
        _registry = registry;
        _store = store;
        _credentials = credentials;
        _handlers = handlers;
        _tools = tools;
        _services = services;
        _options = options;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<ConnectorHost>();
        _sink = sink;
        _cursors = cursors;
        _time = time ?? TimeProvider.System;
    }

    public static string ToolSource(string connectionId) => $"connector:{connectionId}";

    public IReadOnlyList<ConnectorRuntimeInfo> Snapshot() =>
        _info.Values.OrderBy(i => i.ConnectorId).ThenBy(i => i.Label).ToList();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reconcileEvery = TimeSpan.FromSeconds(Math.Max(1, _options.ReconcileSeconds));
        var nextReconcile = DateTimeOffset.MinValue;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), _time);
        try
        {
            do
            {
                var now = _time.GetUtcNow();
                try
                {
                    if (now >= nextReconcile)
                    {
                        await ReconcileAsync(stoppingToken);
                        nextReconcile = now + reconcileEvery;
                    }

                    var due = _running.Values.Where(r => r.NextWork <= now).ToList();
                    await Task.WhenAll(due.Select(r => WorkAsync(r, stoppingToken)));
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "The connector host loop failed; it will try again");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        foreach (var id in _running.Keys.ToList())
            await StopConnectionAsync(id);
    }

    /// <summary>
    /// Brings the running connectors in line with the stored connections: starts new
    /// ones, stops removed ones, and restarts one whose settings or label changed.
    /// </summary>
    public async Task ReconcileAsync(CancellationToken ct)
    {
        await _reconcileGate.WaitAsync(ct);
        try
        {
            var connections = _store.List();
            var prefixes = AssignPrefixes(connections);
            var live = connections.ToDictionary(c => c.Id);

            foreach (var id in _running.Keys.ToList())
            {
                if (!live.TryGetValue(id, out var current)
                    || Fingerprint(current, prefixes.GetValueOrDefault(id)) != _running[id].Fingerprint)
                {
                    await StopConnectionAsync(id);
                }
            }

            foreach (var id in _info.Keys.Where(id => !live.ContainsKey(id)).ToList())
            {
                _info.TryRemove(id, out _);
                _failures.TryRemove(id, out _);
            }

            foreach (var connection in connections)
            {
                if (_running.ContainsKey(connection.Id))
                    continue;

                await StartConnectionAsync(connection, prefixes.GetValueOrDefault(connection.Id), ct);
            }
        }
        finally
        {
            _reconcileGate.Release();
        }
    }

    /// <summary>Checks one connection now and returns what the page should show.</summary>
    public async Task<ConnectorRuntimeInfo?> CheckNowAsync(string connectionId, CancellationToken ct)
    {
        if (_running.TryGetValue(connectionId, out var running))
            await CheckAsync(running, ct);
        return _info.GetValueOrDefault(connectionId);
    }

    public async Task<WebhookOutcome> HandleWebhookAsync(string connectionId, WebhookRequest request, CancellationToken ct)
    {
        if (!_running.TryGetValue(connectionId, out var running)
            || running.Connector is not IEventSourceConnector source)
        {
            return WebhookOutcome.NotFound;
        }

        if (!source.VerifyWebhook(request, out var reason))
        {
            _logger.LogWarning("Rejected a webhook for connection {ConnectionId}: {Reason}", connectionId, reason ?? "verification failed");
            return WebhookOutcome.Unauthorized;
        }

        IReadOnlyList<ConnectorEvent> events;
        try
        {
            events = source.ParseWebhook(request);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "A webhook for connection {ConnectionId} could not be parsed", connectionId);
            return WebhookOutcome.BadRequest;
        }

        return Publish(connectionId, events) ? WebhookOutcome.Accepted : WebhookOutcome.Busy;
    }

    private bool Publish(string connectionId, IEnumerable<ConnectorEvent> events)
    {
        if (_sink is null)
            return true;

        var accepted = true;
        foreach (var evt in events)
        {
            if (_sink.Publish(connectionId, evt) == ConnectorEventOutcome.Rejected)
                accepted = false;
        }
        return accepted;
    }

    private async Task StartConnectionAsync(Connection connection, string? prefix, CancellationToken ct)
    {
        var definition = _registry.Get(connection.ConnectorId);
        var descriptor = definition?.Descriptor;
        prefix ??= descriptor?.ToolPrefix ?? connection.ConnectorId;

        void Report(ConnectorState state, string detail, int tools = 0) =>
            _info[connection.Id] = new ConnectorRuntimeInfo(
                connection.Id, connection.ConnectorId, connection.Label, prefix, state, detail, tools,
                descriptor?.Capabilities ?? ConnectorCapabilities.None, _time.GetUtcNow());

        if (definition is null)
        {
            Report(ConnectorState.NotConfigured, $"No connector named '{connection.ConnectorId}' is installed.");
            return;
        }

        if (connection.Status == ConnectionStatus.NeedsReauth)
        {
            Report(ConnectorState.NeedsReauth, connection.StatusDetail ?? "Connect the account again.");
            return;
        }

        if (_failures.TryGetValue(connection.Id, out var failure) && failure.RetryAt > _time.GetUtcNow()
            && failure.Fingerprint == Fingerprint(connection, prefix))
        {
            return;
        }

        var hosts = definition.AllowedHostsFor(connection.Settings);
        var context = new ConnectionContext(
            connection, definition.Descriptor, prefix, hosts, _credentials, _handlers,
            TimeSpan.FromSeconds(Math.Max(1, _options.HttpTimeoutSeconds)),
            _loggerFactory.CreateLogger($"AiAgentCanvas.Connector.{definition.Descriptor.Id}"));

        IConnector? connector = null;
        try
        {
            connector = definition.Create(context);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.OperationTimeoutSeconds)));
            await connector.StartAsync(cts.Token);

            var running = new Running(connection, connector, Fingerprint(connection, prefix), prefix, definition.Descriptor.Capabilities)
            {
                NextWork = _time.GetUtcNow(),
            };
            _running[connection.Id] = running;
            _failures.TryRemove(connection.Id, out _);

            if (connector is IToolConnector toolConnector)
            {
                await RegisterToolsAsync(running, toolConnector, ct);
                running.ToolsChanged = (_, _) => _ = RefreshToolsSafelyAsync(running, toolConnector);
                toolConnector.ToolsChanged += running.ToolsChanged;
            }

            _logger.LogInformation("Started connector {Connector} for connection {ConnectionId} ({Label})",
                definition.Descriptor.Id, connection.Id, connection.Label);
            Report(ConnectorState.Connected, "started", running.ToolCount);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (connector is not null)
                await DisposeQuietly(connector);
            throw;
        }
        catch (Exception ex)
        {
            if (connector is not null)
                await DisposeQuietly(connector);
            _running.TryRemove(connection.Id, out _);
            _tools.Unregister(ToolSource(connection.Id));

            var attempts = (_failures.TryGetValue(connection.Id, out var previous) ? previous.Attempts : 0) + 1;
            var delay = TimeSpan.FromSeconds(Math.Min(600, 30 * Math.Pow(2, attempts - 1)));
            _failures[connection.Id] = new Failure(attempts, _time.GetUtcNow() + delay, Fingerprint(connection, prefix));

            _logger.LogWarning(ex, "Connector {Connector} could not start for connection {ConnectionId}; retrying in {Delay}",
                definition.Descriptor.Id, connection.Id, delay);
            Report(ConnectorState.Error, $"Could not start: {ex.Message}");
        }
    }

    private async Task StopConnectionAsync(string connectionId)
    {
        if (!_running.TryRemove(connectionId, out var running))
            return;

        _tools.Unregister(ToolSource(connectionId));
        if (running.Connector is IToolConnector tc && running.ToolsChanged is not null)
            tc.ToolsChanged -= running.ToolsChanged;

        await DisposeQuietly(running.Connector);
    }

    private async Task DisposeQuietly(IConnector connector)
    {
        try
        {
            await connector.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "A connector failed while shutting down");
        }
    }

    private async Task RegisterToolsAsync(Running running, IToolConnector connector, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.OperationTimeoutSeconds)));
        var raw = await connector.GetToolsAsync(cts.Token);

        var prepared = raw.Select(PrepareTool).ToList();
        running.ToolCount = prepared.Count;
        _tools.Register(ToolSource(running.Connection.Id), prepared);
    }

    private async Task RefreshToolsSafelyAsync(Running running, IToolConnector connector)
    {
        try
        {
            await RegisterToolsAsync(running, connector, CancellationToken.None);
            UpdateInfo(running, _info.GetValueOrDefault(running.Connection.Id)?.State ?? ConnectorState.Connected,
                _info.GetValueOrDefault(running.Connection.Id)?.Detail ?? "ok");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not refresh the tools of connection {ConnectionId}", running.Connection.Id);
        }
    }

    /// <summary>
    /// Governance and tracing wrap the tool the way they wrap a built-in one. A tool that
    /// reaches another person or cannot be undone is wrapped last, so approval is asked
    /// before anything else runs.
    /// </summary>
    private AITool PrepareTool(AITool tool)
    {
        var risk = ConnectorTools.RiskOf(tool);
        var wrapped = AgentPipeline.WrapTools(_services, [tool])[0];

        if (wrapped is AIFunction function
            && _options.ApprovalMode == ConnectorApprovalMode.Require
            && risk is ToolRisk.Send or ToolRisk.Destructive)
        {
            return new ApprovalRequiredAIFunction(function);
        }

        return wrapped;
    }

    private async Task WorkAsync(Running running, CancellationToken ct)
    {
        // Claim the slot first so a slow connector is not started twice.
        var now = _time.GetUtcNow();
        running.NextWork = now.AddSeconds(Math.Max(1, Math.Min(_options.CheckIntervalSeconds, _options.PollIntervalSeconds)));

        if (running.NextCheck <= now)
        {
            running.NextCheck = now.AddSeconds(Math.Max(1, _options.CheckIntervalSeconds));
            await CheckAsync(running, ct);
        }

        if (running.Connector is IEventSourceConnector source && running.NextPoll <= now)
        {
            running.NextPoll = now.AddSeconds(Math.Max(1, _options.PollIntervalSeconds));
            await PollAsync(running, source, ct);
        }
    }

    private async Task CheckAsync(Running running, CancellationToken ct)
    {
        ConnectorStatus status;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.OperationTimeoutSeconds)));
            status = await running.Connector.CheckAsync(cts.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            status = ConnectorStatus.Error($"The check failed: {ex.Message}");
        }

        var id = running.Connection.Id;
        UpdateInfo(running, status.State, status.Detail);

        switch (status.State)
        {
            case ConnectorState.NeedsReauth:
                // Marking the connection stops its tools on the next reconcile.
                await _credentials.MarkNeedsReauthAsync(id, status.Detail, ct);
                break;

            case ConnectorState.Error:
                if (_store.Get(id)?.Status == ConnectionStatus.Connected)
                    _store.SetStatus(id, ConnectionStatus.Error, status.Detail);
                break;

            case ConnectorState.Connected:
                if (_store.Get(id)?.Status == ConnectionStatus.Error)
                    _store.SetStatus(id, ConnectionStatus.Connected, null);
                break;
        }
    }

    /// <summary>Polls one connection now. The loop does this on a timer; tests call it directly.</summary>
    internal async Task PollNowAsync(string connectionId, CancellationToken ct)
    {
        if (_running.TryGetValue(connectionId, out var running) && running.Connector is IEventSourceConnector source)
            await PollAsync(running, source, ct);
    }

    private async Task PollAsync(Running running, IEventSourceConnector source, CancellationToken ct)
    {
        if (_sink is null || _cursors is null)
            return;

        var key = $"connector:{running.Connection.Id}";
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.OperationTimeoutSeconds)));
            var batch = await source.PollAsync(_cursors.Get(key), cts.Token);

            // Hold the cursor when a subscriber is full so the same events come back
            // next time. The queue de-duplicates by event id, so a replay does no harm.
            if (Publish(running.Connection.Id, batch.Events) && batch.NextCursor is not null)
                _cursors.Set(key, batch.NextCursor);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Polling connection {ConnectionId} failed", running.Connection.Id);
        }
    }

    private void UpdateInfo(Running running, ConnectorState state, string detail) =>
        _info[running.Connection.Id] = new ConnectorRuntimeInfo(
            running.Connection.Id,
            running.Connection.ConnectorId,
            running.Connection.Label,
            running.Prefix,
            state,
            detail,
            running.ToolCount,
            running.Capabilities,
            _time.GetUtcNow());

    private static string Fingerprint(Connection connection, string? prefix) =>
        string.Join('\n',
            connection.Label,
            prefix ?? "",
            connection.Status == ConnectionStatus.NeedsReauth ? "reauth" : "ok",
            JsonSerializer.Serialize(connection.Settings.OrderBy(p => p.Key, StringComparer.Ordinal)));

    /// <summary>
    /// The first connection of a connector uses the plain tool prefix. Later ones add
    /// their label, so two accounts of the same connector do not share tool names.
    /// </summary>
    private Dictionary<string, string> AssignPrefixes(IReadOnlyList<Connection> connections)
    {
        var result = new Dictionary<string, string>();
        var used = new HashSet<string>();

        foreach (var group in connections.GroupBy(c => c.ConnectorId))
        {
            var definition = _registry.Get(group.Key);
            if (definition is null)
                continue;

            var basePrefix = definition.Descriptor.ToolPrefix;
            var first = true;
            foreach (var connection in group.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal))
            {
                var prefix = basePrefix;
                if (!first)
                {
                    var slug = Slug(connection.Label);
                    prefix = $"{basePrefix}_{(slug.Length == 0 ? connection.Id : slug)}";
                    if (prefix.Length > 40)
                        prefix = prefix[..40].TrimEnd('_');
                }

                var candidate = prefix;
                for (var n = 2; !used.Add(candidate); n++)
                    candidate = $"{prefix}_{n}";

                result[connection.Id] = candidate;
                first = false;
            }
        }

        return result;
    }

    internal static string Slug(string label)
    {
        var builder = new StringBuilder(label.Length);
        foreach (var ch in label.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
                builder.Append(ch);
            else if (builder.Length > 0 && builder[^1] != '_')
                builder.Append('_');
        }
        return builder.ToString().Trim('_');
    }

    private sealed class Running(Connection connection, IConnector connector, string fingerprint, string prefix, ConnectorCapabilities capabilities)
    {
        public Connection Connection { get; } = connection;
        public IConnector Connector { get; } = connector;
        public string Fingerprint { get; } = fingerprint;
        public string Prefix { get; } = prefix;
        public ConnectorCapabilities Capabilities { get; } = capabilities;
        public int ToolCount { get; set; }
        public EventHandler? ToolsChanged { get; set; }
        public DateTimeOffset NextWork { get; set; }
        public DateTimeOffset NextCheck { get; set; }
        public DateTimeOffset NextPoll { get; set; }
    }

    private sealed record Failure(int Attempts, DateTimeOffset RetryAt, string Fingerprint);
}
