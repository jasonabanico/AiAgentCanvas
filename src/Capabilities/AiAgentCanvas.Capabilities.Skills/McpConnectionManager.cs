using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;
using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Orchestration.Skills;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace AiAgentCanvas.Capabilities.Skills;

public sealed class McpConnectionManager : IAsyncDisposable
{
    private readonly DynamicToolRegistry _registry;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, McpConnection> _connections = new();
    private readonly ConcurrentDictionary<string, McpConnectionConfig> _connectionConfigs = new();
    private readonly Timer _healthTimer;

    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromMinutes(2);

    public McpConnectionManager(DynamicToolRegistry registry, ILoggerFactory loggerFactory)
    {
        _registry = registry;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<McpConnectionManager>();
        _healthTimer = new Timer(OnHealthCheck, null, HealthCheckInterval, HealthCheckInterval);
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(ConnectMcpServer, "connect_mcp_server",
                "Connect to an MCP server and register its tools"),
            AIFunctionFactory.Create(DisconnectMcpServer, "disconnect_mcp_server",
                "Disconnect from an MCP server and remove its tools"),
            AIFunctionFactory.Create(ListMcpConnections, "list_mcp_connections",
                "List all active MCP server connections and their health status"),
        ];
    }

    public IReadOnlyList<AITool> GetMcpTools()
    {
        return _connections.Values
            .Where(c => c.Tools is not null)
            .SelectMany(c => c.Tools!)
            .ToList();
    }

    public async Task ConnectAsync(IMcpConnectionSeed seed, CancellationToken ct)
    {
        await ConnectAsync(
            seed.Name, seed.Endpoint, seed.Transport,
            seed.BearerToken, seed.ApiKey, seed.ExpectedIssuer,
            seed.AdditionalHeaders, ct);
    }

    public async Task ConnectAsync(
        string name,
        string endpoint,
        string transport,
        string? bearerToken = null,
        string? apiKey = null,
        string? expectedIssuer = null,
        IDictionary<string, string>? additionalHeaders = null,
        CancellationToken ct = default)
    {
        if (_connections.ContainsKey(name))
            return;

        _connectionConfigs[name] = new McpConnectionConfig
        {
            Name = name,
            Endpoint = endpoint,
            Transport = transport,
            BearerToken = bearerToken,
            ApiKey = apiKey,
            ExpectedIssuer = expectedIssuer,
            AdditionalHeaders = additionalHeaders,
        };

        var transportOptions = BuildTransportOptions(name, endpoint, bearerToken, apiKey, additionalHeaders);
        IClientTransport clientTransport = transport.ToLowerInvariant() switch
        {
            "http" or "sse" => new HttpClientTransport(transportOptions, _loggerFactory),
            _ => throw new ArgumentException($"Unsupported transport type: {transport}"),
        };

        var client = await McpClient.CreateAsync(clientTransport, cancellationToken: ct);

        if (!string.IsNullOrEmpty(expectedIssuer))
        {
            var serverInfo = client.ServerInfo;
            if (serverInfo is not null && !string.IsNullOrEmpty(serverInfo.Name)
                && !string.Equals(serverInfo.Name, expectedIssuer, StringComparison.OrdinalIgnoreCase))
            {
                await client.DisposeAsync();
                throw new InvalidOperationException(
                    $"MCP server identity mismatch for '{name}': expected issuer '{expectedIssuer}', got '{serverInfo.Name}'");
            }
        }

        var mcpTools = await client.ListToolsAsync(cancellationToken: ct);
        var aiTools = mcpTools.Cast<AITool>().ToList();

        var connection = new McpConnection
        {
            Name = name,
            Endpoint = endpoint,
            Transport = transport,
            Client = client,
            Tools = aiTools,
            ConnectedAt = DateTimeOffset.UtcNow,
            LastHealthCheck = DateTimeOffset.UtcNow,
            IsHealthy = true,
        };

        _connections[name] = connection;
        _registry.Register($"mcp:{name}", aiTools);

        _logger.LogInformation(
            "Connected to MCP server {Name} at {Endpoint}, {ToolCount} tools registered, auth={AuthType}",
            name, endpoint, aiTools.Count, GetAuthType(bearerToken, apiKey));
    }

    [Description("Connect to an MCP server and register its tools")]
    private async Task<string> ConnectMcpServer(
        [Description("A unique name for this connection")] string name,
        [Description("The server endpoint URL")] string endpoint,
        [Description("Transport type: 'http' or 'sse'")] string transport,
        [Description("Bearer token for authentication (optional)")] string? bearerToken,
        [Description("API key for authentication (optional)")] string? apiKey,
        CancellationToken ct)
    {
        if (_connections.ContainsKey(name))
            return JsonSerializer.Serialize(new { error = $"Connection '{name}' already exists" });

        try
        {
            await ConnectAsync(name, endpoint, transport, bearerToken, apiKey, ct: ct);
            var toolNames = _connections[name].Tools!.Select(t => t.Name).ToList();
            return JsonSerializer.Serialize(new
            {
                status = "connected", name, endpoint,
                toolCount = toolNames.Count, tools = toolNames,
                auth = GetAuthType(bearerToken, apiKey),
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to MCP server {Name} at {Endpoint}", name, endpoint);
            return JsonSerializer.Serialize(new { error = $"Failed to connect: {ex.Message}" });
        }
    }

    [Description("Disconnect from an MCP server and remove its tools")]
    private async Task<string> DisconnectMcpServer(
        [Description("Name of the connection to disconnect")] string name,
        CancellationToken ct)
    {
        if (!_connections.TryRemove(name, out var connection))
            return JsonSerializer.Serialize(new { error = $"Connection '{name}' not found" });

        _connectionConfigs.TryRemove(name, out _);
        _registry.Unregister($"mcp:{name}");

        if (connection.Client is not null)
            await connection.Client.DisposeAsync();

        _logger.LogInformation("Disconnected from MCP server {Name}", name);
        return JsonSerializer.Serialize(new { status = "disconnected", name });
    }

    [Description("List all active MCP server connections and their health status")]
    private string ListMcpConnections()
    {
        var connections = _connections.Values.Select(c => new
        {
            c.Name,
            c.Endpoint,
            c.Transport,
            toolCount = c.Tools?.Count ?? 0,
            c.IsHealthy,
            connectedAt = c.ConnectedAt,
            lastHealthCheck = c.LastHealthCheck,
        }).ToList();

        return JsonSerializer.Serialize(new { count = connections.Count, connections });
    }

    private void OnHealthCheck(object? state)
    {
        _ = RunHealthChecksAsync();
    }

    private async Task RunHealthChecksAsync()
    {
        foreach (var (name, connection) in _connections)
        {
            try
            {
                if (connection.Client is McpClient mcpClient)
                {
                    await mcpClient.PingAsync();
                    connection.IsHealthy = true;
                    connection.LastHealthCheck = DateTimeOffset.UtcNow;
                }
            }
            catch (Exception ex)
            {
                connection.IsHealthy = false;
                connection.LastHealthCheck = DateTimeOffset.UtcNow;
                _logger.LogWarning(ex, "Health check failed for MCP server {Name}, attempting reconnection", name);

                await TryReconnectAsync(name);
            }
        }
    }

    private async Task TryReconnectAsync(string name)
    {
        if (!_connectionConfigs.TryGetValue(name, out var config))
            return;

        if (_connections.TryRemove(name, out var oldConnection))
        {
            _registry.Unregister($"mcp:{name}");
            if (oldConnection.Client is not null)
            {
                try { await oldConnection.Client.DisposeAsync(); }
                catch { /* already broken */ }
            }
        }

        try
        {
            await ConnectAsync(
                config.Name, config.Endpoint, config.Transport,
                config.BearerToken, config.ApiKey, config.ExpectedIssuer,
                config.AdditionalHeaders, CancellationToken.None);
            _logger.LogInformation("Reconnected to MCP server {Name}", name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reconnection to MCP server {Name} failed", name);
        }
    }

    private static HttpClientTransportOptions BuildTransportOptions(
        string name,
        string endpoint,
        string? bearerToken,
        string? apiKey,
        IDictionary<string, string>? additionalHeaders)
    {
        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri(endpoint),
            Name = name,
            ConnectionTimeout = TimeSpan.FromSeconds(30),
            MaxReconnectionAttempts = 5,
            DefaultReconnectionInterval = TimeSpan.FromSeconds(2),
        };

        Dictionary<string, string>? headers = null;

        if (!string.IsNullOrEmpty(bearerToken))
        {
            headers ??= new();
            headers["Authorization"] = $"Bearer {bearerToken}";
        }
        else if (!string.IsNullOrEmpty(apiKey))
        {
            headers ??= new();
            headers["X-API-Key"] = apiKey;
        }

        if (additionalHeaders is not null)
        {
            headers ??= new();
            foreach (var (key, value) in additionalHeaders)
                headers[key] = value;
        }

        if (headers is not null)
            options.AdditionalHeaders = headers;

        return options;
    }

    private static string GetAuthType(string? bearerToken, string? apiKey)
    {
        if (!string.IsNullOrEmpty(bearerToken)) return "bearer";
        if (!string.IsNullOrEmpty(apiKey)) return "api-key";
        return "none";
    }

    public async ValueTask DisposeAsync()
    {
        await _healthTimer.DisposeAsync();

        foreach (var connection in _connections.Values)
        {
            if (connection.Client is not null)
            {
                try { await connection.Client.DisposeAsync(); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing MCP client {Name}", connection.Name);
                }
            }
        }
        _connections.Clear();
        _connectionConfigs.Clear();
    }

    private sealed class McpConnection
    {
        public string Name { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string Transport { get; set; } = string.Empty;
        public IAsyncDisposable? Client { get; set; }
        public List<AITool>? Tools { get; set; }
        public DateTimeOffset ConnectedAt { get; set; }
        public DateTimeOffset LastHealthCheck { get; set; }
        public bool IsHealthy { get; set; }
    }

    private sealed class McpConnectionConfig
    {
        public string Name { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string Transport { get; set; } = string.Empty;
        public string? BearerToken { get; set; }
        public string? ApiKey { get; set; }
        public string? ExpectedIssuer { get; set; }
        public IDictionary<string, string>? AdditionalHeaders { get; set; }
    }
}
