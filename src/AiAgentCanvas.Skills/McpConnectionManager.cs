using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;
using AiAgentCanvas.Core.Skills;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace AiAgentCanvas.Skills;

public sealed class McpConnectionManager : IAsyncDisposable
{
    private readonly DynamicToolRegistry _registry;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, McpConnection> _connections = new();

    public McpConnectionManager(DynamicToolRegistry registry, ILoggerFactory loggerFactory)
    {
        _registry = registry;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<McpConnectionManager>();
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
                "List all active MCP server connections"),
        ];
    }

    public IReadOnlyList<AITool> GetMcpTools()
    {
        return _connections.Values
            .Where(c => c.Tools is not null)
            .SelectMany(c => c.Tools!)
            .ToList();
    }

    public async Task ConnectAsync(string name, string endpoint, string transport, CancellationToken ct)
    {
        if (_connections.ContainsKey(name))
            return;

        IClientTransport clientTransport = transport.ToLowerInvariant() switch
        {
            "http" or "sse" => new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(endpoint) }),
            _ => throw new ArgumentException($"Unsupported transport type: {transport}"),
        };

        var client = await McpClient.CreateAsync(clientTransport, cancellationToken: ct);
        var mcpTools = await client.ListToolsAsync(cancellationToken: ct);
        var aiTools = mcpTools.Cast<AITool>().ToList();

        var connection = new McpConnection
        {
            Name = name,
            Endpoint = endpoint,
            Transport = transport,
            Client = client,
            Tools = aiTools,
        };

        _connections[name] = connection;
        _registry.Register($"mcp:{name}", aiTools);

        _logger.LogInformation("Connected to MCP server {Name} at {Endpoint}, {ToolCount} tools registered",
            name, endpoint, aiTools.Count);
    }

    [Description("Connect to an MCP server and register its tools")]
    private async Task<string> ConnectMcpServer(
        [Description("A unique name for this connection")] string name,
        [Description("The server endpoint URL")] string endpoint,
        [Description("Transport type: 'http' or 'sse'")] string transport,
        CancellationToken ct)
    {
        if (_connections.ContainsKey(name))
            return JsonSerializer.Serialize(new { error = $"Connection '{name}' already exists" });

        try
        {
            await ConnectAsync(name, endpoint, transport, ct);
            var toolNames = _connections[name].Tools!.Select(t => t.Name).ToList();
            return JsonSerializer.Serialize(new { status = "connected", name, endpoint, toolCount = toolNames.Count, tools = toolNames });
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

        _registry.Unregister($"mcp:{name}");

        if (connection.Client is not null)
        {
            await connection.Client.DisposeAsync();
        }

        _logger.LogInformation("Disconnected from MCP server {Name}", name);
        return JsonSerializer.Serialize(new { status = "disconnected", name });
    }

    [Description("List all active MCP server connections")]
    private string ListMcpConnections()
    {
        var connections = _connections.Values.Select(c => new
        {
            c.Name,
            c.Endpoint,
            c.Transport,
            toolCount = c.Tools?.Count ?? 0,
        }).ToList();

        return JsonSerializer.Serialize(new { count = connections.Count, connections });
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections.Values)
        {
            if (connection.Client is not null)
            {
                try
                {
                    await connection.Client.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing MCP client {Name}", connection.Name);
                }
            }
        }
        _connections.Clear();
    }

    private class McpConnection
    {
        public string Name { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string Transport { get; set; } = string.Empty;
        public IAsyncDisposable? Client { get; set; }
        public List<AITool>? Tools { get; set; }
    }
}
