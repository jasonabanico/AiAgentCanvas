# AI Agents

## Table of Contents

- [What Are AI Agents](#what-are-ai-agents)
  - [Chatbots vs LLM Wrappers vs AI Agents](#chatbots-vs-llm-wrappers-vs-ai-agents)
  - [Key Characteristics of AI Agents](#key-characteristics-of-ai-agents)
  - [The Agent Loop](#the-agent-loop)
  - [Where AI Agent Canvas Fits](#where-ai-agent-canvas-fits)
- [Microsoft Agent Framework](#microsoft-agent-framework)
  - [Core Types](#core-types)
  - [The Agent Execution Model](#the-agent-execution-model)
  - [Middleware Pipeline](#middleware-pipeline)
  - [Running the Agent](#running-the-agent)
- [Tools and Skills](#tools-and-skills)
  - [What Are AI Tools](#what-are-ai-tools)
  - [The Tool Call Loop](#the-tool-call-loop)
  - [DynamicToolRegistry](#dynamictoolregistry)
  - [Tool Providers](#tool-providers)
  - [Skills: Agent-Managed Tools](#skills-agent-managed-tools)
  - [Tool Design Guidelines](#tool-design-guidelines)
- [Model Context Protocol](#model-context-protocol)
  - [Why MCP Matters](#why-mcp-matters)
  - [MCP in AI Agent Canvas](#mcp-in-ai-agent-canvas)
  - [Dynamic MCP Connections](#dynamic-mcp-connections)
  - [MCP Tools vs Local Tools](#mcp-tools-vs-local-tools)
  - [Tool Composition Example](#tool-composition-example)
  - [Security Considerations](#security-considerations)
- [Context Providers](#context-providers)
  - [What Are Context Providers](#what-are-context-providers)
  - [The Context Injection Chain](#the-context-injection-chain)
  - [Built-in Context Providers](#built-in-context-providers)
  - [Creating Custom Context Providers](#creating-custom-context-providers)
  - [Design Guidelines](#design-guidelines)
- [AG-UI Protocol](#ag-ui-protocol)
  - [What Is AG-UI](#what-is-ag-ui)
  - [The /api/copilotkit Endpoint](#the-apicopilotkit-endpoint)
  - [Request Format](#request-format)
  - [SSE Response Format](#sse-response-format)
  - [Event Types](#event-types)
  - [Streaming Implementation](#streaming-implementation)
  - [How CopilotKit Consumes the Stream](#how-copilotkit-consumes-the-stream)
  - [Why SSE Over WebSockets](#why-sse-over-websockets)
  - [Cancellation](#cancellation)

---

## What Are AI Agents

AI agents represent a fundamental shift from traditional chatbots and simple LLM wrappers. Where a chatbot follows scripted flows and an LLM wrapper sends a prompt and returns a response, an AI agent **autonomously reasons, plans, and takes action** to accomplish goals.

### Chatbots vs LLM Wrappers vs AI Agents

| Capability | Traditional Chatbot | LLM Wrapper | AI Agent |
|---|---|---|---|
| Natural language understanding | Rule-based / intent matching | Full LLM capability | Full LLM capability |
| Decision making | Scripted decision trees | Single inference call | Multi-step reasoning |
| Tool use | None or hardcoded | None | Dynamic tool selection |
| Memory | Session state only | Stateless | Persistent context |
| Autonomy | None | None | Goal-directed behavior |

A **traditional chatbot** matches user input against predefined intents and follows scripted flows. It cannot handle novel requests.

An **LLM wrapper** sends user input to a language model and returns the response. It gains natural language understanding but remains a single request-response cycle with no ability to take action.

An **AI agent** uses an LLM as its reasoning engine while adding the ability to call tools, maintain context across interactions, and execute multi-step plans autonomously.

### Key Characteristics of AI Agents

#### Autonomy

Agents decide **what to do next** without explicit human instruction for each step. Given a goal like "find the latest sales data and summarize trends," an agent determines which tools to call, in what order, and how to synthesize the results.

#### Tool Use

Agents interact with external systems through **tools** (also called functions). A tool might query a database, call an API, search documents, or perform calculations. The LLM decides which tool to invoke based on the user's request and the tool's description.

#### Reasoning

Before acting, agents reason about the problem. Modern LLMs can break down complex requests into sub-tasks, evaluate which approach is most appropriate, and adjust their plan based on intermediate results.

#### Memory and Context

Agents maintain context across interactions. This includes conversation history, retrieved documents (RAG), user preferences, and domain-specific knowledge injected through context providers.

### The Agent Loop

Every AI agent follows a fundamental execution cycle:

```
                +------------------+
                |    PERCEIVE      |
                |  Receive input,  |
                |  gather context  |
                +--------+---------+
                         |
                         v
                +------------------+
                |     REASON       |
                |  Analyze input,  |
                |  plan next step  |
                +--------+---------+
                         |
                         v
                +------------------+
                |      ACT         |
                | Call a tool or   |
                | generate response|
                +--------+---------+
                         |
                         v
                +------------------+
                |    OBSERVE       |
                | Process result,  |
                | decide if done   |
                +--------+---------+
                         |
                    +----+----+
                    |         |
                    v         v
                Continue    Done
                (loop)    (respond)
```

1. **Perceive** -- The agent receives user input along with injected context (system prompts, RAG results, entity data, user profile information).
2. **Reason** -- The LLM analyzes the input and available tools, then decides on the next action. This may involve breaking a complex request into steps.
3. **Act** -- The agent either calls a tool (e.g., querying a database via MCP) or generates a text response. Tool calls are executed by the framework, not the LLM itself.
4. **Observe** -- The tool result is fed back to the LLM. If the task is complete, the agent produces a final response. If not, it loops back to the Reason step.

This loop continues until the agent determines the task is complete or reaches a configured limit.

### Where AI Agent Canvas Fits

AI Agent Canvas is a **multi-agent enterprise copilot platform** built on three pillars:

- **Microsoft Agent Framework (MAF)** for agent orchestration and the LLM execution loop
- **Azure AI Foundry** as the LLM provider (GPT-4o, GPT-4.1, and other models)
- **CopilotKit** with the AG-UI protocol for real-time streaming to the frontend

The platform is designed around separation of concerns:

```
+---------------------------------------------+
|             AiAgentCanvas.Web               |
|          (Composition Root)                  |
+-----+------------------+--------------------+
      |                  |                    |
      v                  v                    v
+----------+     +-----------+     +------------------+
|   Core   |     |    MCP    |     |   MyAgents       |
| (Engine) |     |  (Data)   |     | (Custom Logic)   |
+----------+     +-----------+     +------------------+
```

- **Core** handles the agent execution loop, tool registry, context providers, and AG-UI streaming. It never changes per use case.
- **MCP** provides data connections. Each MCP project implements data access for a specific domain.
- **MyAgents** contains custom agent reasoning logic. Agents are pure reasoning -- no HTTP calls, no SDK imports for data. They access data exclusively through tools provided by MCP connections.

This architecture means you can build enterprise copilots by composing **agents** (reasoning), **MCP connections** (data), and **context providers** (behavioral instructions) without modifying the core platform.

---

## Microsoft Agent Framework

Microsoft Agent Framework (MAF) is the orchestration layer that powers AI Agent Canvas. It provides the abstractions for building agents that can reason, call tools, and stream responses -- all while integrating with the broader Microsoft.Extensions.AI ecosystem.

AI Agent Canvas uses MAF version **1.10.0** from the `Microsoft.Agents.AI` namespace.

### Core Types

#### IChatClient

At the foundation, MAF builds on `IChatClient` from `Microsoft.Extensions.AI`. This interface represents any LLM provider -- Azure AI Foundry, OpenAI, Ollama, or others. AI Agent Canvas configures it through `AIFoundryClientFactory`:

```csharp
services.AddChatClient(sp =>
    sp.GetRequiredService<AIFoundryClientFactory>().CreateChatClient());
```

This abstraction means the platform is not locked to a specific LLM provider. Swapping from Azure AI Foundry to another provider requires only changing the `IChatClient` registration.

#### ChatClientAgent

`ChatClientAgent` is the primary agent implementation in MAF. It wraps an `IChatClient` and adds the agent execution loop: building messages, attaching tools, calling the LLM, handling tool calls, and streaming responses.

```csharp
var agent = new ChatClientAgent(chatClient, agentOptions, loggerFactory, serviceProvider);
```

The `ChatClientAgent` constructor takes four parameters:

| Parameter | Purpose |
|---|---|
| `IChatClient` | The LLM provider to use for inference |
| `ChatClientAgentOptions` | Configuration: name, tools, context providers |
| `ILoggerFactory` | Structured logging for the agent pipeline |
| `IServiceProvider` | DI container for resolving dependencies |

#### ChatClientAgentOptions

This options class configures how the agent behaves:

```csharp
var agentOptions = new ChatClientAgentOptions
{
    Name = "AiAgentCanvas",
    Description = "A multi-tool AI assistant",
    ChatOptions = new ChatOptions { Tools = tools },
    ChatHistoryProvider = chatHistoryProvider,
    AIContextProviders = contextProviders,
};
```

Key properties:

- **Name / Description** -- Identify the agent in logs and multi-agent scenarios
- **ChatOptions** -- Wraps `ChatOptions` from Microsoft.Extensions.AI, including the tool list
- **ChatHistoryProvider** -- Optional provider for loading/saving conversation history
- **AIContextProviders** -- Chain of providers that inject instructions, tools, and context before each LLM call

#### AIAgent

`AIAgent` is the abstract base class for all agents in MAF. `ChatClientAgent` inherits from it. The `AIAgent` type is what gets registered in DI and consumed by endpoints:

```csharp
services.AddSingleton(sp =>
{
    // ... configure ChatClientAgent ...
    return (AIAgent)builder.Build(sp);
});
```

#### AgentSession

Each conversation creates an `AgentSession` that carries per-request state:

```csharp
var session = await agent.CreateSessionAsync(cancellationToken);
session.StateBag.SetValue("conversationId", threadId);
```

The `StateBag` is a key-value store for session-scoped data. Context providers and middleware can read from and write to it during the agent loop.

### The Agent Execution Model

When the agent receives a request, MAF executes the following pipeline:

```
User Message
     |
     v
+--------------------+
| AIContextProviders |  <-- Inject system prompt, RAG, persona, guardrails
| (chain)            |
+--------------------+
     |
     v
+--------------------+
| Build Messages     |  <-- Conversation history + user message + context
+--------------------+
     |
     v
+--------------------+
| Attach Tools       |  <-- Static tools + dynamic tools from registry
+--------------------+
     |
     v
+--------------------+
| Call LLM           |  <-- IChatClient.GetStreamingChatCompletionAsync
+--------------------+
     |
     +------+------+
     |             |
     v             v
  Text          Tool Call
  Response      +------------------+
  (stream)      | Execute Tool     |
                +------------------+
                     |
                     v
                +------------------+
                | Feed Result Back |  <-- Loop to "Call LLM" with tool result
                +------------------+
```

#### Step 1: Context Injection

Before the LLM is called, MAF iterates through the `AIContextProviders` chain. Each provider can modify the `AIContext` -- adding instructions, appending tools, or injecting retrieved documents. Providers execute in registration order.

#### Step 2: Message Construction

The framework combines conversation history (from `ChatHistoryProvider`), the current user message, and the system instructions assembled by context providers into a single message list.

#### Step 3: Tool Attachment

Tools registered in `ChatOptions` and any dynamic tools added by `DynamicToolContextProvider` are attached to the LLM request. The LLM receives the tool schemas (name, description, parameters) so it can decide when to call them.

#### Step 4: LLM Invocation

MAF calls `IChatClient` with the messages and tools. For streaming, it uses `GetStreamingChatCompletionAsync`, yielding tokens as they arrive.

#### Step 5: Tool Call Handling

If the LLM returns a tool call instead of (or alongside) text, MAF:

1. Parses the tool name and arguments from the LLM response
2. Finds the matching `AITool` in the registered set
3. Executes the tool with the provided arguments
4. Appends the tool result as a new message
5. Calls the LLM again with the updated message list

This loop continues until the LLM produces a final text response without further tool calls.

### Middleware Pipeline

MAF supports middleware that wraps the agent execution. AI Agent Canvas uses this for logging:

```csharp
var builder = agent.AsBuilder();
builder.Use(async (messages, session, runOptions, nextAsync, ct) =>
{
    logger.LogInformation("Agent invoked. Messages={Count}", messages.Count());
    await nextAsync(messages, session, runOptions, ct);
    logger.LogInformation("Agent completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
});
```

Middleware receives the full pipeline context and calls `nextAsync` to continue execution. This pattern is familiar from ASP.NET Core middleware and enables cross-cutting concerns like:

- **Logging** -- Timing and message counts
- **Metrics** -- Token usage and tool call counts
- **Error handling** -- Catching and recovering from LLM failures
- **Caching** -- Short-circuiting repeated queries

### Running the Agent

The agent exposes two execution methods:

- **`RunAsync`** -- Returns the complete response after all tool calls resolve
- **`RunStreamingAsync`** -- Yields response chunks as an `IAsyncEnumerable`, enabling SSE streaming

AI Agent Canvas uses `RunStreamingAsync` in the AG-UI endpoint:

```csharp
await foreach (var update in agent.RunStreamingAsync(
    chatMessages, session, cancellationToken: ct))
{
    if (update.Text is { Length: > 0 } text)
    {
        await WriteEvent(context, "text.message.content",
            new { messageId, delta = text });
    }
}
```

### MAF Summary

MAF provides the execution backbone for AI Agent Canvas:

| Concern | MAF Type |
|---|---|
| LLM abstraction | `IChatClient` |
| Agent orchestration | `ChatClientAgent` / `AIAgent` |
| Configuration | `ChatClientAgentOptions` |
| Context injection | `AIContextProvider` chain |
| Session state | `AgentSession` / `StateBag` |
| Extensibility | Middleware pipeline via `AsBuilder()` |

The framework handles the complexity of the agent loop -- tool call resolution, message management, streaming -- so that application code focuses on defining tools, context providers, and business logic.

---

## Tools and Skills

Tools are how AI agents interact with the world. Without tools, an agent can only generate text. With tools, it can query databases, call APIs, search documents, manage schedules, and perform calculations. Skills extend this concept by allowing agents to create and manage reusable prompt-based tools at runtime.

### What Are AI Tools

In the Microsoft.Extensions.AI ecosystem, a tool is represented by the `AITool` base class. The most common concrete type is `AIFunction`, which wraps a .NET method so the LLM can call it.

The LLM never executes tools directly. Instead, it receives tool **schemas** (name, description, parameter definitions) and decides when to call them. The platform handles execution and feeds results back.

#### AIFunction and AIFunctionFactory

`AIFunctionFactory.Create` is the primary way to define tools in AI Agent Canvas. It takes a delegate and metadata, producing an `AITool` the platform can register:

```csharp
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
```

The `[Description]` attribute on parameters generates the JSON schema that the LLM uses to understand what arguments to provide:

```csharp
[Description("Connect to an MCP server and register its tools")]
private async Task<string> ConnectMcpServer(
    [Description("A unique name for this connection")] string name,
    [Description("The server endpoint URL")] string endpoint,
    [Description("Transport type: 'http' or 'sse'")] string transport,
    CancellationToken ct)
{
    // Implementation
}
```

Good tool descriptions are critical. The LLM relies on them to decide **when** and **how** to call a tool. Vague descriptions lead to incorrect tool selection.

### The Tool Call Loop

When the LLM decides to use a tool, the following sequence executes:

```
User: "What MCP servers are connected?"
                    |
                    v
           +----------------+
           |   LLM Reasons  |
           | "I should call |
           | list_mcp_      |
           | connections"   |
           +-------+--------+
                   |
                   v
           +----------------+
           | Platform       |
           | executes       |
           | ListMcp...()   |
           +-------+--------+
                   |
                   v
           +----------------+
           | Result:        |
           | {"count": 2,   |
           |  "connections":.|
           |  [...]}        |
           +-------+--------+
                   |
                   v
           +----------------+
           | LLM receives   |
           | result, formats|
           | human-readable |
           | response       |
           +----------------+
```

Key points about tool execution:

1. **The LLM chooses** -- The platform never forces a tool call. The LLM infers from context and tool descriptions.
2. **The platform executes** -- Tool methods run in the server process, not in the LLM. This is important for security and data access.
3. **Results feed back** -- Tool output becomes a new message in the conversation, and the LLM generates its response with that information.
4. **Multiple rounds** -- The LLM can call multiple tools in sequence, using each result to inform the next step.

### DynamicToolRegistry

Not all tools are known at startup. AI Agent Canvas uses `DynamicToolRegistry` to compose tools from multiple sources at runtime:

```csharp
public sealed class DynamicToolRegistry
{
    private readonly ConcurrentDictionary<string, List<AITool>> _toolsBySource = new();

    public void Register(string source, IEnumerable<AITool> tools)
    {
        _toolsBySource[source] = tools.ToList();
    }

    public void Unregister(string source)
    {
        _toolsBySource.TryRemove(source, out _);
    }

    public IReadOnlyList<AITool> GetAllTools()
    {
        return _toolsBySource.Values.SelectMany(t => t).ToList();
    }
}
```

Tools are grouped by **source** -- a string key that identifies where they came from. For example:

- `"mcp:market-data"` -- Tools from a connected MCP server
- `"skills"` -- Tools loaded from skill definitions
- `"system"` -- Built-in system tools

The `DynamicToolContextProvider` injects these dynamic tools into every LLM call:

```csharp
internal sealed class DynamicToolContextProvider : AIContextProvider
{
    private readonly DynamicToolRegistry _registry;

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        var dynamicTools = _registry.GetAllTools();
        if (dynamicTools.Count > 0)
        {
            var existing = context.AIContext.Tools?.ToList() ?? [];
            existing.AddRange(dynamicTools);
            context.AIContext.Tools = existing;
        }
        return new ValueTask<AIContext>(context.AIContext);
    }
}
```

This means when an MCP server is connected at runtime, its tools immediately become available to the agent without a restart.

### Tool Providers

AI Agent Canvas organizes tools into **tool provider** classes. Each provider owns a specific domain of tools:

| Provider | Tools | Purpose |
|---|---|---|
| `McpConnectionManager` | `connect_mcp_server`, `disconnect_mcp_server`, `list_mcp_connections` | Manage MCP data connections |
| `SkillToolProvider` | `create_skill`, `list_skills`, `run_skill`, `remove_skill` | Manage reusable prompt skills |
| `SkillAuthoringToolProvider` | `author_skill`, `edit_skill`, `read_skill`, `delete_authored_skill` | Author skill markdown files |
| `SchedulerToolProvider` | Schedule-related tools | Manage scheduled agent tasks |
| `WorkflowToolProvider` | Workflow tools | Execute multi-step workflows |
| `SystemToolProvider` | System-level utilities | Core system operations |

Tool providers implement a `GetTools()` method that returns their tools as `IReadOnlyList<AITool>`. These are registered in DI and collected during agent construction.

### Skills: Agent-Managed Tools

Skills are a higher-level abstraction on top of tools. A skill is a **persisted prompt template** that the agent can create, manage, and execute at runtime.

#### Creating a Skill

When a user asks the agent to "create a skill that summarizes text," the agent calls `create_skill`:

```csharp
private string CreateSkill(string name, string description, string promptTemplate)
{
    var record = new SkillRecord
    {
        Id = id,
        Name = normalizedName,
        Description = description,
        PromptTemplate = promptTemplate,  // e.g., "Summarize: {input}"
    };
    _store.SaveSkill(record);
}
```

#### Running a Skill

When the skill is invoked via `run_skill`, the platform substitutes the `{input}` placeholder and sends the expanded prompt to the agent:

```csharp
private async Task<string> RunSkill(string name, string input, CancellationToken ct)
{
    var skill = _store.GetSkill(name);
    var prompt = skill.PromptTemplate.Replace("{input}", input);

    var messages = new List<ChatMessage>
    {
        new(ChatRole.User, prompt),
    };

    var response = await _agent.RunAsync(messages, cancellationToken: ct);
    return JsonSerializer.Serialize(new { skill = name, result = response.Text });
}
```

#### Skill Authoring

Beyond runtime skill creation, `SkillAuthoringToolProvider` allows the agent to write skill definitions as **markdown files** with YAML frontmatter:

```yaml
---
name: summarize
description: Summarize text into key points
tags: text, summary
---

Summarize the following text into 3-5 key bullet points:

{input}
```

This persistence format makes skills portable, version-controllable, and human-readable.

### Tool Design Guidelines

When building tools for AI Agent Canvas:

1. **Descriptive names** -- Use verb-noun format: `connect_mcp_server`, not `mcp` or `connect`.
2. **Clear descriptions** -- Both the tool and each parameter need descriptions the LLM can reason about.
3. **Return structured data** -- Return JSON strings so the LLM can parse and present results cleanly.
4. **Handle errors gracefully** -- Return error information as data rather than throwing exceptions.
5. **Keep tools focused** -- One tool per action. Avoid multi-purpose tools with mode parameters.
6. **Use CancellationToken** -- Long-running tools should accept and respect cancellation tokens.

### Tools and Skills Summary

The tools and skills system in AI Agent Canvas provides a layered approach:

- **AITool / AIFunction** -- The foundation: .NET methods the LLM can call
- **Tool Providers** -- Organized groups of related tools
- **DynamicToolRegistry** -- Runtime composition of tools from multiple sources
- **Skills** -- Persisted prompt templates the agent manages as reusable capabilities

---

## Model Context Protocol

Model Context Protocol (MCP) is an open standard for connecting LLMs to external data sources, tools, and services. Rather than building custom integrations for every data source, MCP provides a **universal protocol** that any LLM-powered application can use to discover and call tools exposed by any MCP-compliant server.

### Why MCP Matters

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

### MCP in AI Agent Canvas

AI Agent Canvas implements MCP through two main components:

- **`McpConnectionManager`** -- Manages the lifecycle of MCP connections (connect, disconnect, list)
- **`DynamicToolRegistry`** -- Registers MCP tools so the agent can use them alongside local tools

#### McpClient and HttpClientTransport

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

#### Tool Discovery

Once connected, the client discovers available tools:

```csharp
var mcpTools = await client.ListToolsAsync(cancellationToken: ct);
var aiTools = mcpTools.Cast<AITool>().ToList();
```

MCP tools implement `AITool`, so they integrate seamlessly with the Microsoft.Extensions.AI tool system. The agent sees no difference between a local tool and an MCP tool -- both appear in the same tool list with the same schema format.

### Dynamic MCP Connections

A key design principle of AI Agent Canvas is that MCP connections are **runtime-dynamic**. The agent itself can connect to and disconnect from MCP servers during a conversation.

#### Connecting

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

#### Disconnecting

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

#### Listing Connections

The `list_mcp_connections` tool shows all active connections and their tool counts, giving the agent (and user) visibility into what data sources are available.

### MCP Tools vs Local Tools

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

### Tool Composition Example

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

### Security Considerations

Dynamic MCP connections introduce security concerns that AI Agent Canvas addresses through its governance layer.

#### SSRF Protection

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

#### Best Practices

When deploying MCP in production:

1. **Allowlist endpoints** -- Only permit connections to known, trusted MCP server URLs
2. **Use HTTPS** -- Always require TLS for MCP transport
3. **Audit connections** -- Log all connect/disconnect events and tool invocations
4. **Scope permissions** -- Use the governance policy to restrict tool access per agent
5. **Set timeouts** -- Configure connection and request timeouts to prevent hanging connections
6. **Dispose properly** -- The `McpConnectionManager` implements `IAsyncDisposable` to clean up connections on shutdown

### MCP Summary

MCP transforms how AI agents access data. Instead of hardcoded integrations, agents discover and use tools from any MCP-compliant server at runtime. AI Agent Canvas's implementation makes this dynamic and governed:

- **`McpConnectionManager`** handles the connection lifecycle
- **`DynamicToolRegistry`** merges MCP tools with local tools transparently
- **`GovernedMcpGateway`** enforces security policies on MCP tool calls
- The agent can connect/disconnect MCP servers during a conversation, adapting to the user's needs

---

## Context Providers

Context providers are the mechanism by which AI Agent Canvas shapes agent behavior **without modifying the agent itself**. They inject instructions, domain knowledge, guardrails, and user-specific information into the agent's context before every LLM call.

### What Are Context Providers

A context provider is a subclass of `AIContextProvider` from the Microsoft Agent Framework (`Microsoft.Agents.AI`). Each provider implements a single method that can modify the `AIContext` -- adding to the system instructions, attaching tools, or injecting retrieved documents:

```csharp
public abstract class AIContextProvider
{
    protected abstract ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken);
}
```

The `InvokingContext` gives the provider access to:

- **`AIContext`** -- The current context being built (instructions, tools, messages)
- **Session state** -- Via the agent session's `StateBag`

Providers return the modified `AIContext`, which flows to the next provider in the chain.

### The Context Injection Chain

Context providers execute in **registration order** before every LLM call. Each provider layers its contribution on top of what previous providers set:

```
LLM Call Request
     |
     v
+------------------------+
| SystemPromptProvider   |  --> Sets base system instructions
+------------------------+
     |
     v
+------------------------+
| PlannerContext...      |  --> Decomposes complex requests into step plans
+------------------------+
     |
     v
+------------------------+
| PersonaContextProvider |  --> Appends persona-specific instructions
+------------------------+
     |
     v
+------------------------+
| UserProfileContext...  |  --> Appends user preferences and profile data
+------------------------+
     |
     v
+------------------------+
| EntityContextProvider  |  --> Appends entity/domain knowledge index
+------------------------+
     |
     v
+------------------------+
| GuardrailContext...    |  --> Appends behavioral rules and constraints
+------------------------+
     |
     v
+------------------------+
| RagContextProvider     |  --> Appends retrieved documents from vector search
+------------------------+
     |
     v
+------------------------+
| GovernanceContext...   |  --> Scans for prompt injection, audits
+------------------------+
     |
     v
+------------------------+
| DynamicToolContext...  |  --> Attaches runtime tools from DynamicToolRegistry
+------------------------+
     |
     v
  Final AIContext sent to LLM
```

This layered approach means each provider is **independent and composable**. Adding a new provider does not require changes to existing ones.

### Built-in Context Providers

#### SystemPromptProvider

The foundation of the context chain. Sets the base system instructions if none are already set:

```csharp
internal sealed class SystemPromptProvider : AIContextProvider
{
    private readonly string _systemPrompt;

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(context.AIContext.Instructions))
            context.AIContext.Instructions = _systemPrompt;
        return new ValueTask<AIContext>(context.AIContext);
    }
}
```

The default prompt is configurable via `AiAgentCanvasOptions.SystemPrompt` or defaults to a generic helpful assistant prompt.

#### PlanningMiddleware (Goal Decomposition)

Provides **persistent goal decomposition** for complex multi-step requests. Implemented as agent middleware (not a context provider) so it has access to the session's `StateBag` for plan persistence across messages.

On each user message, the middleware follows this logic:

1. **No existing plan:** Makes a lightweight LLM call (MaxOutputTokens=300, Temperature=0) to decide whether the request needs a multi-step plan. Simple requests (1-2 tool calls) get `NO_PLAN` and pass through unchanged. Complex requests get a numbered execution plan generated and stored in `StateBag`.
2. **Existing plan found:** Makes a continuation call that examines the conversation history to determine which steps are complete. Returns `COMPLETED: 1, 2 / REMAINING: 3, 4 / NEXT: 3` to track progress, `ALL_DONE` to clear the plan, or `REPLAN` if the user changed direction.

```csharp
public sealed class PlanningMiddleware
{
    public async Task InvokeAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? runOptions,
        Func<...> nextAsync,
        CancellationToken ct)
    {
        // Check StateBag for existing plan
        session?.StateBag.TryGetValue<string>("planner:active_plan", out var existingPlan);

        if (existingPlan is not null)
            planToInject = await EvaluateContinuationAsync(existingPlan, messages, ct);
        else
            planToInject = await GenerateNewPlanAsync(lastUserMessage, ct);

        // Persist plan to StateBag for next message
        session?.StateBag.SetValue("planner:active_plan", planToInject);

        // Inject plan into messages as system message
        messageList.Insert(0, new ChatMessage(ChatRole.System, plan));
        await nextAsync(messageList, session, runOptions, ct);
    }
}
```

The planner is aware of all registered tools (both static startup tools and dynamic MCP tools) and references them by name in the generated plan. The plan persists across messages via `StateBag`, enabling multi-turn workflows where the agent picks up where it left off. If the user changes direction mid-plan, the continuation evaluator detects this and generates a fresh plan.

#### PersonaContextProvider

Appends persona-specific instructions from the `PersonaStore`. Personas define **how** the agent should behave -- tone, expertise level, domain focus:

```csharp
internal sealed class PersonaContextProvider : AIContextProvider
{
    private readonly PersonaStore _store;

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        var activeInstructions = _store.GetActiveInstructions();
        if (!string.IsNullOrEmpty(activeInstructions))
        {
            context.AIContext.Instructions =
                (context.AIContext.Instructions ?? "") + "\n" + activeInstructions;
        }
        return new ValueTask<AIContext>(context.AIContext);
    }
}
```

Example persona instructions might be: "You are a financial analyst specializing in equity markets. Use precise numbers and cite data sources. Respond in a professional, concise tone."

#### UserProfileContextProvider

Injects user-specific preferences and information:

```csharp
internal sealed class UserProfileContextProvider : AIContextProvider
{
    private readonly UserProfileStore _store;

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        var profileContext = _store.LoadActiveProfileContext();
        if (!string.IsNullOrEmpty(profileContext))
        {
            context.AIContext.Instructions =
                (context.AIContext.Instructions ?? "") + profileContext;
        }
        return new ValueTask<AIContext>(context.AIContext);
    }
}
```

This enables personalization without the user repeating preferences. The profile might include role, team, preferred output format, or domain-specific terminology.

#### EntityContextProvider

Appends a domain knowledge index from the `EntityStore`. Entities represent key concepts, terms, or reference data the agent should know about:

```csharp
internal sealed class EntityContextProvider : AIContextProvider
{
    private readonly EntityStore _store;

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        var index = _store.LoadEntityIndex();
        if (!string.IsNullOrEmpty(index))
        {
            context.AIContext.Instructions =
                (context.AIContext.Instructions ?? "") + index;
        }
        return new ValueTask<AIContext>(context.AIContext);
    }
}
```

For example, an entity index might define company-specific product names, internal acronyms, or organizational structure that the LLM would not know from training data.

#### GuardrailContextProvider

Appends behavioral constraints and rules:

```csharp
internal sealed class GuardrailContextProvider : AIContextProvider
{
    private readonly GuardrailStore _store;

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        var rules = _store.LoadActiveRules();
        if (!string.IsNullOrEmpty(rules))
        {
            context.AIContext.Instructions =
                (context.AIContext.Instructions ?? "") + rules;
        }
        return new ValueTask<AIContext>(context.AIContext);
    }
}
```

Guardrail rules might include: "Never provide specific investment advice. Always include disclaimers when discussing financial data. Do not share PII across user sessions."

#### RagContextProvider

The most sophisticated provider in the chain. It retrieves relevant documents from a vector store using a multi-stage pipeline:

1. **Hybrid search** -- combines vector cosine similarity (70%) with FTS5 keyword/BM25 scoring (30%) via the `IHybridSearchable` interface. Falls back to vector-only search if the store doesn't support hybrid.
2. **Metadata filtering** -- optionally filters by `Source` (exact match) and `Tags` (contains match) before scoring, reducing the candidate set.
3. **LLM reranking** -- sends the top-10 candidates to the LLM to re-score by relevance, then keeps the top-3. Falls back to original ranking on failure.
4. **Citation formatting** -- each result is numbered with its source and score, and the LLM is instructed to cite by number.

```csharp
public sealed class RagContextProvider : AIContextProvider
{
    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        // 1. Embed the user's query
        var queryEmbedding = await _embeddingGenerator
            .GenerateVectorAsync(lastUserMessage, cancellationToken: cancellationToken);

        // 2. Hybrid search (vector + keyword) with metadata filters
        if (_collection is IHybridSearchable hybridStore)
            await foreach (var result in hybridStore.HybridSearchAsync(...))
                candidates.Add(result);

        // 3. LLM reranking: top-10 -> top-3
        var results = _reranker is not null
            ? await _reranker.RerankAsync(query, candidates, topK)
            : candidates.Take(topK).ToList();

        // 4. Format with citations: [1] (source: X, score: 0.82)
        context.AIContext.Instructions +=
            "Relevant context (cite by number):\n" + ragContext;
    }
}
```

RAG differs from other providers in that it is **query-dependent** -- it reads the last user message, generates an embedding, and searches a vector store for relevant content.

#### GovernanceContextProvider

Scans the assembled instructions for prompt injection attempts:

```csharp
public sealed class GovernanceContextProvider : AIContextProvider
{
    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        var result = _kernel.InjectionDetector.Detect(context.AIContext.Instructions);
        if (result.IsInjection)
        {
            _logger.LogWarning(
                "[GOVERNANCE] Prompt injection detected: {Type}",
                result.InjectionType);

            _kernel.AuditEmitter.Emit(
                GovernanceEventType.PolicyViolation, ...);
        }
        return new ValueTask<AIContext>(context.AIContext);
    }
}
```

This provider typically runs **last** in the chain so it can scan the fully assembled instructions.

#### DynamicToolContextProvider

Injects runtime tools from the `DynamicToolRegistry`. Unlike other providers that modify instructions, this one adds **tools**:

```csharp
internal sealed class DynamicToolContextProvider : AIContextProvider
{
    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        var dynamicTools = _registry.GetAllTools();
        if (dynamicTools.Count > 0)
        {
            var existing = context.AIContext.Tools?.ToList() ?? [];
            existing.AddRange(dynamicTools);
            context.AIContext.Tools = existing;
        }
        return new ValueTask<AIContext>(context.AIContext);
    }
}
```

### Creating Custom Context Providers

To add a custom context provider, subclass `AIContextProvider` and register it in DI:

```csharp
public sealed class CompanyKnowledgeProvider : AIContextProvider
{
    private readonly IKnowledgeBase _kb;

    public CompanyKnowledgeProvider(IKnowledgeBase kb) => _kb = kb;

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        var knowledge = await _kb.GetRelevantFacts(cancellationToken);
        if (!string.IsNullOrEmpty(knowledge))
        {
            context.AIContext.Instructions =
                (context.AIContext.Instructions ?? "") +
                $"\n\nCompany knowledge:\n{knowledge}";
        }
        return context.AIContext;
    }
}
```

Register it in `Program.cs`:

```csharp
services.AddSingleton<AIContextProvider, CompanyKnowledgeProvider>();
```

The provider will execute as part of the chain, in the order it was registered.

### Design Guidelines

1. **Single responsibility** -- Each provider should handle one concern (persona, guardrails, RAG, etc.)
2. **Append, don't replace** -- Append to existing instructions rather than overwriting them, unless you are the system prompt provider
3. **Fail gracefully** -- If a provider's data source is unavailable, return the context unchanged rather than throwing
4. **Keep instructions concise** -- Every token in the instructions counts against the context window and adds cost
5. **Order matters** -- Register providers in logical order: base prompt first, then layered context, then guardrails, then governance scanning last

---

## AG-UI Protocol

AG-UI (Agent Graphical UI) is a protocol for streaming AI agent responses to frontend applications in real time. AI Agent Canvas uses AG-UI to connect the .NET backend (powered by Microsoft Agent Framework) to the CopilotKit frontend over Server-Sent Events (SSE).

### What Is AG-UI

Traditional REST APIs return a complete response in a single payload. For AI agents that may take seconds to generate a response, this creates a poor user experience -- the user stares at a loading spinner until the entire response is ready.

AG-UI solves this by streaming **events** as the agent processes. The user sees text appear token by token, knows when the agent starts and finishes, and gets a responsive conversational experience.

The protocol defines a set of event types that represent the lifecycle of an agent response:

```
Client POST /api/copilotkit
     |
     v
Server opens SSE stream
     |
     +-- event: run.started
     +-- event: text.message.start
     +-- event: text.message.content  (repeated, one per token chunk)
     +-- event: text.message.content
     +-- event: text.message.content
     +-- event: text.message.end
     +-- event: run.finished
     |
     v
SSE stream closes
```

### The /api/copilotkit Endpoint

AI Agent Canvas exposes a single SSE endpoint that CopilotKit connects to:

```csharp
public static class AgUiEndpoint
{
    public static void MapAgUiEndpoints(this WebApplication app)
    {
        app.MapPost("/api/copilotkit", HandleCopilotKitRequest);
    }
}
```

This endpoint is mapped in the application startup via `app.UseAiAgentCanvas()`, which internally calls `app.MapAgUiEndpoints()`.

### Request Format

The frontend sends a POST request with a JSON body containing the conversation messages and an optional thread ID:

```json
{
  "threadId": "conv-abc-123",
  "messages": [
    {
      "role": "system",
      "content": "You are a helpful assistant."
    },
    {
      "role": "user",
      "content": "What is the current market summary?"
    }
  ]
}
```

The endpoint extracts messages and initializes the agent session:

```csharp
var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);
var messages = body.GetProperty("messages");
var threadId = body.TryGetProperty("threadId", out var tid)
    ? tid.GetString() ?? Guid.NewGuid().ToString()
    : Guid.NewGuid().ToString();
var runId = Guid.NewGuid().ToString();
```

Each request gets a unique `runId`. The `threadId` persists across messages in the same conversation, enabling continuity.

### SSE Response Format

The response uses the `text/event-stream` content type with standard SSE formatting:

```csharp
context.Response.ContentType = "text/event-stream";
context.Response.Headers.CacheControl = "no-cache";
context.Response.Headers.Connection = "keep-alive";
```

Each event is written as:

```
event: <event-type>
data: <json-payload>
```

Note the double newline after the data line -- this is the SSE event delimiter.

### Event Types

#### run.started

Signals that the agent has begun processing the request:

```
event: run.started
data: {"threadId":"conv-abc-123","runId":"run-xyz-789"}
```

The frontend uses this to show a processing indicator and track the run.

#### text.message.start

Marks the beginning of a new assistant message:

```
event: text.message.start
data: {"messageId":"msg-001","role":"assistant","agentName":"AiAgentCanvas"}
```

CopilotKit creates a new message bubble in the UI when it receives this event.

#### text.message.content

Delivers a chunk of the response text. This event is sent repeatedly as tokens stream from the LLM:

```
event: text.message.content
data: {"messageId":"msg-001","delta":"The current"}

event: text.message.content
data: {"messageId":"msg-001","delta":" market shows"}

event: text.message.content
data: {"messageId":"msg-001","delta":" strong momentum..."}
```

The `delta` field contains the incremental text. The frontend appends each delta to build the complete message progressively.

#### text.message.end

Signals that the message is complete:

```
event: text.message.end
data: {"messageId":"msg-001"}
```

#### run.finished

Signals that the entire agent run is complete:

```
event: run.finished
data: {"threadId":"conv-abc-123","runId":"run-xyz-789"}
```

The frontend uses this to stop the processing indicator and re-enable the input field.

### Streaming Implementation

The backend iterates over the agent's streaming output and emits SSE events for each chunk:

```csharp
await foreach (var update in agent.RunStreamingAsync(
    chatMessages, session, cancellationToken: context.RequestAborted))
{
    if (update.Text is { Length: > 0 } text)
    {
        await WriteEvent(context, "text.message.content",
            new { messageId, delta = text });
    }
}
```

The `WriteEvent` helper formats and flushes each event:

```csharp
private static async Task WriteEvent(
    HttpContext context, string eventType, object data)
{
    var json = JsonSerializer.Serialize(data, JsonOptions);
    await context.Response.WriteAsync(
        $"event: {eventType}\ndata: {json}\n\n",
        context.RequestAborted);
    await context.Response.Body.FlushAsync(context.RequestAborted);
}
```

The `FlushAsync` call is critical -- without it, the response may be buffered and the client would not receive events in real time.

### How CopilotKit Consumes the Stream

On the frontend, CopilotKit handles the SSE stream automatically. The Next.js application configures CopilotKit with the backend URL:

```jsx
<CopilotKit runtimeUrl="/api/copilotkit">
  <CopilotChat />
</CopilotKit>
```

CopilotKit:

1. Sends POST requests to `/api/copilotkit` when the user submits a message
2. Opens an SSE connection on the response
3. Parses incoming events by type
4. Updates the chat UI progressively as `text.message.content` events arrive
5. Manages conversation state across multiple request/response cycles

### Why SSE Over WebSockets

AI Agent Canvas uses SSE rather than WebSockets for several reasons:

| Consideration | SSE | WebSockets |
|---|---|---|
| Direction | Server-to-client (unidirectional) | Bidirectional |
| HTTP compatibility | Standard HTTP, works with all proxies/CDNs | Requires protocol upgrade |
| Reconnection | Built-in automatic reconnect | Must implement manually |
| Complexity | Simple text protocol | Binary framing, connection management |
| Load balancers | Works with standard HTTP load balancers | Requires sticky sessions or special config |
| Firewall friendliness | Standard HTTPS port, no special rules | May be blocked by corporate firewalls |

For the agent use case, communication is fundamentally **request-response with streaming**: the client sends a message (POST), and the server streams back the response. There is no need for the server to push unsolicited messages or for the client to send data mid-stream.

SSE fits this pattern naturally:

- The POST request carries the user message
- The SSE response carries the streaming agent response
- HTTP infrastructure (proxies, CDNs, load balancers) works without modification
- Automatic reconnection handles network interruptions

WebSockets would add complexity (connection management, heartbeats, reconnection logic) without providing benefits for this use case.

### Cancellation

The endpoint respects the HTTP request's cancellation token:

```csharp
cancellationToken: context.RequestAborted
```

If the client closes the connection (user navigates away, network drops), the `RequestAborted` token fires, canceling the agent's LLM call and stopping the stream. This prevents wasted compute on responses nobody will read.

### AG-UI Summary

The AG-UI protocol provides a clean boundary between the .NET agent backend and the CopilotKit frontend:

- **POST /api/copilotkit** -- Single endpoint for all agent interactions
- **SSE events** -- `run.started`, `text.message.start`, `text.message.content`, `text.message.end`, `run.finished`
- **Progressive rendering** -- Users see responses as they are generated, token by token
- **Standard HTTP** -- No special infrastructure required, works with existing proxies and load balancers
- **Cancellation** -- Clean shutdown when clients disconnect

---

> **[Download the complete PDF guide](guides/AI-Agent-Canvas-Guide.pdf)** | **[AI-First Company Guide](guides/AI-First-Company-Guide.pdf)**
