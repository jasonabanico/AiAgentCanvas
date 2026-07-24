# 7. Expanding with MCP and RAG

Agents start with the tools and knowledge you seed at build time. MCP and RAG extend the agent at runtime -- MCP connects external tools and services without writing code, and RAG enriches every response with relevant documents from a vector store.

## Model Context Protocol (MCP)

### What MCP Does

MCP is an open standard for connecting AI agents to external tools and data sources at runtime. Instead of writing a tool provider class and redeploying, you point the agent at an MCP server endpoint and it discovers the available tools automatically. The tools appear alongside built-in tools and the LLM can call them like any other function.

### Connecting via Agent Tools

The agent exposes three MCP management tools through the `McpConnectionManager`:

```
connect_mcp_server(name, endpoint, transport)   -- connect and discover tools
disconnect_mcp_server(name)                     -- disconnect and remove tools
list_mcp_connections()                          -- list active connections
```

A user can connect to an MCP server through conversation:

> "Connect to the GitHub MCP server at https://mcp.github.com/sse"

The agent calls `connect_mcp_server` with the name, endpoint, and transport type. From that point on, the GitHub server's tools are available in the conversation.

### How Connection Works

The `McpConnectionManager` handles the full connection lifecycle:

1. The LLM calls `connect_mcp_server` with a name, endpoint URL, and transport type
2. The manager creates a transport client based on the specified type (stdio, SSE, or streamable HTTP)
3. It connects via `McpClient.CreateAsync()` and calls `ListToolsAsync()` to discover the server's tools
4. Discovered tools are registered into the `DynamicToolRegistry` under the key `mcp:{name}`
5. The `DynamicToolContextProvider` picks up the new tools on the next LLM invocation

```csharp
private async Task<string> ConnectMcpServer(string name, string endpoint,
    string transport, CancellationToken ct)
{
    IClientTransport clientTransport = transport.ToLowerInvariant() switch
    {
        "http" or "sse" => new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(endpoint) }),
        _ => throw new ArgumentException($"Unsupported transport: {transport}"),
    };

    var client = await McpClient.CreateAsync(clientTransport, cancellationToken: ct);
    var mcpTools = await client.ListToolsAsync(cancellationToken: ct);

    // Register discovered tools into the dynamic tool registry
    _dynamicToolRegistry.Register($"mcp:{name}", mcpTools);

    return $"Connected to '{name}' -- {mcpTools.Count} tools discovered.";
}
```

Once connected, MCP tools appear alongside built-in tools with no distinction from the LLM's perspective. The agent can call them, and they show up in tool listings.

### Governance

Every MCP tool call passes through the `GovernedMcpGateway` before execution. The gateway evaluates the call against the agent's active guardrails and policies:

```csharp
public McpGatewayDecision Evaluate(string agentId, string toolName, string? payload = null)
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
```

This means an MCP tool that violates a guardrail is blocked the same way a built-in tool would be. The governance layer does not distinguish between tool sources.

### Building an MCP Connection Seed

To auto-connect to an MCP server at startup (instead of requiring the user to connect manually), implement `IMcpConnectionSeed`:

```csharp
services.AddSingleton<IMcpConnectionSeed>(new McpConnectionSeed(
    name: "company-data",
    endpoint: "https://mcp.internal.example.com/sse",
    transport: "sse"));
```

The interface:

```csharp
public interface IMcpConnectionSeed
{
    string Name { get; }
    string Endpoint { get; }
    string Transport { get; }  // "sse", "http", or "stdio"
}
```

When the platform starts, it resolves all `IMcpConnectionSeed` services and connects to each one automatically. The agent is ready to use those tools from the first request, with no user intervention.

## Retrieval-Augmented Generation (RAG)

### What RAG Does

RAG retrieves relevant document chunks at query time and injects them into the agent's context. Instead of fine-tuning a model on your documents or cramming everything into the system prompt, RAG searches a vector store for the chunks most relevant to the user's current question and includes only those. The agent gets grounded, up-to-date information with source citations.

### The Pipeline

Documents flow through an ingestion pipeline that prepares them for search, and queries flow through a retrieval pipeline that finds and ranks the best matches:

```
Ingestion                              Retrieval
=========                              =========

Raw Document                           User Message
    |                                      |
    v                                      v
DocumentChunker                        Embed Query
(recursive paragraph/sentence split)   (IEmbeddingGenerator)
    |                                      |
    v                                      v
Embed Each Chunk                       Hybrid Search
(IEmbeddingGenerator)                  (vector cosine + FTS5 keyword)
    |                                      |
    v                                      v
Store in SQLite                        LLM Reranking
(vector BLOB + FTS5 index)             (top-10 candidates -> top-3)
                                           |
                                           v
                                       Citation Formatting
                                       ([1] source: X, score: 0.82)
                                           |
                                           v
                                       Inject into Agent Context
```

### Chunking

The `DocumentChunker` recursively splits documents into chunks suitable for embedding. It follows a semantic hierarchy rather than using fixed-size character windows:

1. **Split by paragraphs** (double newlines)
2. If a paragraph exceeds `ChunkSize`, **split by sentences** (period, exclamation, or question mark followed by space or newline)
3. Apply **overlap** between consecutive chunks for context continuity
4. Discard chunks under 20 characters

```csharp
public sealed class DocumentChunker
{
    public int ChunkSize { get; init; } = 512;
    public int ChunkOverlap { get; init; } = 64;

    public List<DocumentChunk> Chunk(string text, string? source = null)
    {
        // 1. Split by paragraphs
        // 2. If paragraph > ChunkSize, split by sentences
        // 3. Apply overlap between consecutive chunks
        // 4. Filter out chunks < 20 chars
    }
}
```

| Property | Default | Description |
|----------|---------|-------------|
| `ChunkSize` | 512 | Maximum characters per chunk |
| `ChunkOverlap` | 64 | Characters of overlap between consecutive chunks |

### Embedding

Each chunk is embedded into a vector using Azure AI Foundry's embedding model. The platform uses `IEmbeddingGenerator` from Microsoft.Extensions.AI, so the embedding provider is swappable via DI. The same embedding model is used for both document ingestion and query-time embedding to ensure consistent vector space alignment.

### Storage

Chunks are stored as `DocumentRecord` entries in a SQLite database with two indexing strategies:

- **Vector BLOB** -- the embedding vector stored as a binary blob for cosine similarity search
- **FTS5 full-text index** -- the chunk text indexed with SQLite's FTS5 extension for keyword/BM25 search

This dual storage enables hybrid search without requiring a separate search engine.

### Hybrid Search

The retrieval pipeline combines two search signals:

- **Vector search** (cosine similarity) -- captures semantic meaning. A query about "company revenue" matches chunks discussing "annual income" or "top-line growth" even without exact keyword overlap.
- **Keyword search** (SQLite FTS5 / BM25) -- captures exact term matches. Important for proper nouns, ticker symbols, product names, and technical terms that semantic search alone might miss.

The scores from both signals are combined to produce a unified ranking. This hybrid approach consistently outperforms either signal alone.

### LLM Reranking

After hybrid search returns the top candidates (typically 10), the platform sends them to the LLM for relevance reranking. The LLM evaluates each candidate against the original query and assigns a relevance score. Only the top-K survivors (typically 3) are kept for injection.

This step is expensive (it costs an additional LLM call) but significantly improves precision. The initial retrieval stage optimizes for recall -- casting a wide net -- while reranking optimizes for precision -- keeping only the truly relevant chunks.

### Context Injection

Surviving chunks are formatted with citation numbers and injected into the agent's context:

```
[1] source: quarterly-report-q3.pdf, score: 0.92
Revenue grew 15% year-over-year to $2.1B, driven primarily by cloud services...

[2] source: earnings-call-transcript.pdf, score: 0.87
The CEO noted that enterprise customer count increased by 23% in Q3...
```

The agent can reference these citations in its response, giving users a clear trail back to the source documents.

### Enabling RAG

RAG is opt-in and requires an embedding model deployment. Set the deployment name in configuration:

```json
{
  "AIFoundry": {
    "EmbeddingDeploymentName": "text-embedding-ada-002"
  }
}
```

When this value is set, the platform auto-registers all RAG components at startup:

- `DocumentChunker` for splitting documents
- `IEmbeddingGenerator` configured against the Azure AI Foundry embedding deployment
- SQLite vector store with FTS5 indexing
- Hybrid search and reranking pipeline
- RAG context provider that injects retrieved chunks before each LLM call

When the value is not set, RAG components are not registered and the agent operates without document retrieval. No code changes needed either way -- the presence of the configuration key is the only switch.
