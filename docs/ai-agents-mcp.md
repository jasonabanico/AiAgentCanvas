> [AI Agents](ai-agents.md) > AI Agents: Model Context Protocol

# AI Agents: Model Context Protocol

Model Context Protocol (MCP) is an open standard for connecting LLMs to external data sources, tools, and services. Rather than building custom integrations for every data source, MCP provides a **universal protocol** that any LLM-powered application can use to discover and call tools exposed by any MCP-compliant server.

## Why MCP Matters

Before MCP, connecting an AI agent to a data source required:

1. Writing custom API client code
2. Defining tool schemas manually
3. Handling authentication, serialization, and error handling per integration
4. Rebuilding when the data source API changes

MCP standardizes this into a single protocol:

```
+----------+          MCP Protocol          +-----------+
|  Agent   | <----------------------------> | MCP Server|
| (Client) |   discover tools, call them,   | (Data     |
|          |   receive results              |  Source)  |
+----------+                                +-----------+
```

An MCP server exposes a set of tools with standardized schemas. An MCP client (the agent) discovers those tools at runtime, and the LLM can call them like any local tool. The agent does not need to know the implementation details of the data source.

## MCP in AI Agent Canvas

AI Agent Canvas implements MCP through two main components:

- **`McpConnectionManager`** -- Manages the lifecycle of MCP connections (connect, disconnect, list)
- **`DynamicToolRegistry`** -- Registers MCP tools so the agent can use them alongside local tools

### McpClient and HttpClientTransport

The platform uses the `ModelContextProtocol.Client` library to connect to MCP servers:

```csharp
IClientTransport clientTransport = transport.ToLowerInvariant() switch
{
    "http" or "sse" => new HttpClientTransport(
        new HttpClientTransportOptions
        {
            Endpoint = new Uri(endpoint)
        }),
    _ => throw new ArgumentException($"Unsupported transport: {transport}"),
};

var client = await McpClient.CreateAsync(clientTransport, cancellationToken: ct);
```

`HttpClientTransport` handles the HTTP-based communication with MCP servers, supporting both standard HTTP request/response and SSE (Server-Sent Events) streaming for real-time tool execution.

### Tool Discovery

Once connected, the client discovers available tools:

```csharp
var mcpTools = await client.ListToolsAsync(cancellationToken: ct);
var aiTools = mcpTools.Cast<AITool>().ToList();
```

MCP tools implement `AITool`, so they integrate seamlessly with the Microsoft.Extensions.AI tool system. The agent sees no difference between a local tool and an MCP tool -- both appear in the same tool list with the same schema format.

## Dynamic MCP Connections

A key design principle of AI Agent Canvas is that MCP connections are **runtime-dynamic**. The agent itself can connect to and disconnect from MCP servers during a conversation.

### Connecting

The agent exposes a `connect_mcp_server` tool that users can invoke through natural language:

```
User: "Connect to the market data server at https://mcp.example.com/market"
```

The agent calls `connect_mcp_server`, which:

1. Creates an `HttpClientTransport` with the specified endpoint
2. Establishes the MCP connection via `McpClient.CreateAsync`
3. Discovers available tools via `ListToolsAsync`
4. Registers the tools in `DynamicToolRegistry` under the source key `mcp:{name}`
5. Returns the list of discovered tools to the LLM

```csharp
_connections[name] = connection;
_registry.Register($"mcp:{name}", aiTools);
```

From this point forward, the agent can call any tool from the connected MCP server.

### Disconnecting

When a connection is no longer needed:

```csharp
private async Task<string> DisconnectMcpServer(string name, CancellationToken ct)
{
    if (!_connections.TryRemove(name, out var connection))
        return JsonSerializer.Serialize(new { error = $"Connection '{name}' not found" });

    _registry.Unregister($"mcp:{name}");

    if (connection.Client is not null)
        await connection.Client.DisposeAsync();

    return JsonSerializer.Serialize(new { status = "disconnected", name });
}
```

Disconnecting removes the tools from the registry and disposes the MCP client. The agent immediately loses access to those tools.

### Listing Connections

The `list_mcp_connections` tool shows all active connections and their tool counts, giving the agent (and user) visibility into what data sources are available.

## MCP Tools vs Local Tools

AI Agent Canvas uses both MCP tools and local tools. Understanding the distinction helps when designing the system:

| Aspect | Local Tools | MCP Tools |
|---|---|---|
| Definition | C# methods via `AIFunctionFactory` | Defined by remote MCP server |
| Registration | At startup in `Program.cs` | At runtime via `McpConnectionManager` |
| Execution | In-process | Remote HTTP call to MCP server |
| Latency | Minimal | Network-dependent |
| Availability | Always available | Only while connected |
| Schema source | `[Description]` attributes | MCP server's tool listing |

From the LLM's perspective, both types are identical. The platform abstracts the execution mechanism, so the agent reasons about tools purely based on their names and descriptions.

## Tool Composition Example

A typical AI Agent Canvas deployment might have:

```
Agent Tool List
|
+-- Local Tools (always available)
|   +-- create_skill
|   +-- run_skill
|   +-- connect_mcp_server
|   +-- list_mcp_connections
|
+-- MCP: market-data (connected at runtime)
|   +-- get_stock_price
|   +-- get_market_summary
|   +-- search_tickers
|
+-- MCP: crm (connected at runtime)
    +-- search_contacts
    +-- get_deal_pipeline
    +-- update_opportunity
```

The `DynamicToolRegistry` merges all sources into a flat list. The LLM sees all tools equally and picks the right one based on the user's request.

## Security Considerations

Dynamic MCP connections introduce security concerns that AI Agent Canvas addresses through its governance layer.

### SSRF Protection

The `GovernedMcpGateway` evaluates every MCP tool call against a governance policy before execution:

```csharp
public sealed class GovernedMcpGateway
{
    private readonly McpGateway _gateway;

    public McpGatewayDecision Evaluate(
        string agentId, string toolName, string? payload = null)
    {
        var request = new McpGatewayRequest
        {
            AgentId = agentId,
            ToolName = toolName,
            Payload = payload ?? "",
        };

        var decision = _gateway.ProcessRequest(request);

        if (!decision.Allowed)
        {
            _logger.LogWarning(
                "[GOVERNANCE:MCP] Blocked tool={Tool} agent={Agent} status={Status}",
                toolName, agentId, decision.Status);
        }

        return decision;
    }
}
```

This prevents:

- **SSRF attacks** -- An attacker cannot trick the agent into connecting to internal network endpoints
- **Unauthorized tool use** -- Policies can restrict which agents can call which MCP tools
- **Data exfiltration** -- Outbound tool calls are audited and can be blocked based on payload inspection

### Best Practices

When deploying MCP in production:

1. **Allowlist endpoints** -- Only permit connections to known, trusted MCP server URLs
2. **Use HTTPS** -- Always require TLS for MCP transport
3. **Audit connections** -- Log all connect/disconnect events and tool invocations
4. **Scope permissions** -- Use the governance policy to restrict tool access per agent
5. **Set timeouts** -- Configure connection and request timeouts to prevent hanging connections
6. **Dispose properly** -- The `McpConnectionManager` implements `IAsyncDisposable` to clean up connections on shutdown

## MCP Summary

MCP transforms how AI agents access data. Instead of hardcoded integrations, agents discover and use tools from any MCP-compliant server at runtime. AI Agent Canvas's implementation makes this dynamic and governed:

- **`McpConnectionManager`** handles the connection lifecycle
- **`DynamicToolRegistry`** merges MCP tools with local tools transparently
- **`GovernedMcpGateway`** enforces security policies on MCP tool calls
- The agent can connect/disconnect MCP servers during a conversation, adapting to the user's needs

---

