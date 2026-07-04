> [Developer Guide](developer-guide.md) > Architecture

# Developer Guide: Architecture

AI Agent Canvas is a .NET multi-agent copilot platform built on Microsoft's Agent Framework (MAF) and Microsoft.Extensions.AI. It connects a CopilotKit frontend to Azure AI Foundry models through an AG-UI Server-Sent Events protocol, with dynamic tool registration, governance, and markdown-persisted agent data.

## Request Flow

```
User (Browser)
    |
    v
CopilotKit Frontend (Next.js)
    |  POST /api/copilotkit (AG-UI protocol)
    v
AgUiEndpoint (SSE)
    |  Extracts messages, creates ChatClientAgent session
    v
ChatClientAgent (MAF)
    |  AIContextProviders inject: system prompt, persona,
    |  guardrails, entities, context, dynamic tools
    v
Azure AI Foundry (LLM)
    |  Model decides which tools to call
    v
Tool Execution
    |  DynamicToolRegistry resolves registered AITools
    |  (MarketData, Skills, MCP connections, AgentData CRUD, etc.)
    v
Streaming Response
    |  SSE events: run.started -> text.message.start ->
    |  text.message.content (deltas) -> text.message.end -> run.finished
    v
CopilotKit renders response
```

## Platform vs Custom Separation

The solution enforces a strict boundary between platform code and business logic.

**Platform projects** (under `src/AiAgentCanvas.*`) provide the engine: orchestration, tool registry, agent data persistence, skills management, security, scheduling, and notifications. These never change per use case.

**Custom projects** (under `src/Agents/` and `src/DataConnections/`) hold all business-specific code: agent prompts, MCP data connections, custom vector stores. You add your own projects here without modifying any platform code.

```
src/
  AiAgentCanvas/                     <-- SDK library
    AiAgentCanvas.Abstractions/      <-- Shared interfaces (no dependencies)
    AiAgentCanvas.Core/              <-- Orchestration engine
    AiAgentCanvas.AgentData/         <-- MD-persisted agent data (7 domains)
    AiAgentCanvas.Skills/            <-- Skill store + MCP connections
    AiAgentCanvas.Security/          <-- Governance + rate limiting
    AiAgentCanvas.Scheduler/         <-- Hangfire scheduled tasks
    AiAgentCanvas.Notifications/     <-- SSE notification channel
    AiAgentCanvas.SystemTools/       <-- Optional file I/O + script tools
  Orchestrator/
    AiAgentCanvas.Orchestrator/      <-- Composition root (Program.cs)
  Agents/
    Agent.HelloWorld/                <-- Starter agent: persona for market data tools
  DataConnections/
    MCP.HelloWorldData/              <-- Sample data connection (SEC EDGAR + Yahoo Finance)
    VectorStore.Sqlite/              <-- SQLite vector store for RAG
```

## Dependency Flow

All projects depend downward toward Abstractions. The Orchestrator is the only project that references everything.

```
                AiAgentCanvas.Orchestrator
                   /    |    |    \    \
                  /     |    |     \    \
            Core   AgentData Skills Security  Scheduler  Notifications
              \       |       |      /
               \      |       |     /
                 Abstractions (shared types)

            MCP.HelloWorldData  ----> Microsoft.Extensions.AI
            Agent.HelloWorld    ----> Abstractions
```

Key dependency rules:

- **Abstractions** has zero project references. It defines `MarkdownFile`, `DocumentRecord`, `INotificationSink`, `IToolGovernanceWrapper`, and the seed interfaces (`IPersonaSeed`, `IGuardrailSeed`, `IWorkflowSeed`, `IContextSeed`, `IEntitySeed`, `IGoalSeed`, `ISkillSeed`, `IMcpConnectionSeed`, `IToolDependencySeed`).
- **Core** references Abstractions. It provides `IChatClient` registration, `DynamicToolRegistry`, `AIContextProvider` base, tool governance wiring, the AG-UI endpoint, and inter-agent communication (`AgentRegistry`, `AgentMailbox`, handoff tools).
- **AgentData, Skills, Security, Scheduler, Notifications** each reference Core and/or Abstractions. AgentData also includes the Goals domain with a SQLite-backed work queue.
- **Agent projects** reference `Abstractions` (for seed interfaces) -- they never reference Core, AgentData, or any other platform project.
- **DataConnection projects** reference `Microsoft.Extensions.AI` (for tool definitions) -- they are independent of the SDK.
- **Orchestrator** references everything and wires it together in `Program.cs`.

## Key Packages

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.Agents.AI | 1.10.0 | ChatClientAgent, AIContextProvider, AIAgent |
| Microsoft.Extensions.AI | 10.7.0 | IChatClient, AITool, AIFunctionFactory, ChatOptions |
| ModelContextProtocol | 1.4.0 | McpClient, HttpClientTransport for MCP servers |
| Microsoft.AgentGovernance | 4.0.0 | GovernanceKernel, PolicyEngine, InjectionDetector |
| Azure.AI.OpenAI | 2.3.0-beta.1 | AzureOpenAIClient for Azure AI Foundry |
| Hangfire | 1.8.23 | Background job scheduling with SQLite storage |

## Extension Method Pattern

Every project exposes its DI registration through `*ServiceExtensions.cs` files with `Add*()` methods and optionally `Use*()` methods for middleware:

```csharp
// Each project follows this pattern:
builder.Services.AddAiAgentCanvasSecurity(builder.Configuration);  // Security first
builder.Services.AddAiAgentCanvas(builder.Configuration, options => { ... });  // Core
builder.Services.AddMarketDataTools();  // Custom tools
builder.Services.AddAiAgentCanvasNotifications();
builder.Services.AddAiAgentCanvasScheduler();
// ... more domain registrations

app.UseAiAgentCanvasSecurity();  // Security middleware
app.UseAiAgentCanvas();          // CORS + AG-UI endpoint + health check
app.MapNotificationEndpoints();  // SSE notification stream
```

Tools are registered as `IReadOnlyList<AITool>` singleton services. The Core platform collects all registered `IReadOnlyList<AITool>` services at startup and passes them to the `ChatClientAgent` via `ChatOptions.Tools`. Additionally, `DynamicToolRegistry` allows runtime tool registration (used by MCP connections).

## Inter-Agent Communication

AI Agent Canvas supports multi-agent collaboration through three mechanisms registered via `AddAiAgentCanvasInterAgentCommunication()` in Core:

- **Agent Registry** -- Builds and caches named `ChatClientAgent` instances from persona definitions. Each persona becomes a resolvable agent with its own instructions. Tools: `list_available_agents`, `get_agent_info`.
- **Agent Mailbox** -- SQLite-backed per-agent message queue (`agentmailbox.db`) for asynchronous communication between agents. Tools: `send_to_agent`, `check_inbox`, `reply_to_message`.
- **Handoff** -- Synchronous delegation: one agent calls `handoff_to_agent` to run a target agent and get the result back in the same turn.

## Autonomous Execution

The Scheduler project includes an autonomous execution mode built on Hangfire. When enabled, a recurring `AutonomousAgentJob` polls the work queue for pending items, claims and executes them via `AIAgent.RunAsync`, and falls back to picking the next active goal when the queue is empty. Goals and work items are managed through the AgentData Goals domain.

Tools: `start_autonomous_mode`, `stop_autonomous_mode`, `get_autonomous_status` (on the Scheduler), plus `create_goal`, `list_goals`, `submit_work_item`, etc. (on AgentData).

---

# Project Structure

This page describes every project in the AI Agent Canvas solution, its purpose, key files, and dependencies.

## Solution Layout

The solution is organized into four top-level folders:

- **AiAgentCanvas** -- SDK library projects that ship as-is
- **Orchestrator** -- the composition root that wires everything together
- **Agents** -- business-specific agent projects you create and modify
- **DataConnections** -- tool providers and vector stores

```
AiAgentCanvas.sln
src/
  AiAgentCanvas/                      SDK library
    AiAgentCanvas.Abstractions/       Shared types (zero dependencies)
    AiAgentCanvas.Core/               Orchestration engine
    AiAgentCanvas.AgentData/          Markdown-persisted agent data
    AiAgentCanvas.Skills/             Skill store + MCP connections
    AiAgentCanvas.Security/           Governance + rate limiting
    AiAgentCanvas.Scheduler/          Hangfire scheduled tasks
    AiAgentCanvas.Notifications/      In-memory notification sink + SSE
    AiAgentCanvas.SystemTools/        Optional file I/O and script tools
  Orchestrator/
    AiAgentCanvas.Orchestrator/       Composition root
  Agents/
    Agent.HelloWorld/                 Starter agent: persona for market data tools
  DataConnections/
    MCP.HelloWorldData/                   Sample data connection (SEC EDGAR + Yahoo Finance)
    VectorStore.Sqlite/               SQLite vector store for RAG
```

## Platform Projects

### AiAgentCanvas.Abstractions

Shared interfaces and utility types with zero project references. Every other project may reference this.

| File | Purpose |
|------|---------|
| `MarkdownFile.cs` | YAML frontmatter + body parsing, serialization, and file loading |
| `DocumentRecord.cs` | Shared document record type |
| `INotificationSink.cs` | Interface for notification delivery |
| `IToolGovernanceWrapper.cs` | Interface for wrapping tools with governance checks |
| `IPersonaSeed.cs` | Seed interface for shipping personas from custom agents |
| `IContextSeed.cs` | Seed interface for shipping context entries from custom agents |
| `IWorkflowSeed.cs` | Seed interface for shipping workflows from custom agents |
| `IEntitySeed.cs` | Seed interface for shipping entities from custom agents |
| `IGuardrailSeed.cs` | Seed interface for shipping guardrails from custom agents |
| `IGoalSeed.cs` | Seed interface for shipping goals from custom agents |
| `ISkillSeed.cs` | Seed interface for shipping skills from custom agents |
| `IMcpConnectionSeed.cs` | Seed interface for declaring MCP connections from custom agents |
| `IToolDependencySeed.cs` | Seed interface for declaring required tools, validated at startup |
| `IAgentMessaging.cs` | Interface for agent-to-agent communication |

`MarkdownFile` is the foundation of agent data persistence. It parses files with YAML frontmatter delimited by `---` and provides `Parse()`, `Write()`, `LoadAll()`, and `SanitizeFileName()` methods.

`IToolGovernanceWrapper` is the bridge between Core and Security. Core optionally resolves it to wrap each tool with governance checks at startup, without referencing the Security project directly.

### AiAgentCanvas.Core

The orchestration engine. Registers the LLM client, tool registry, context providers, and the AG-UI SSE endpoint.

| File | Purpose |
|------|---------|
| `ServiceCollectionExtensions.cs` | `AddAiAgentCanvas()` and `UseAiAgentCanvas()` |
| `Endpoints/AgUiEndpoint.cs` | POST `/api/copilotkit` -- AG-UI SSE streaming |
| `Services/AIFoundryClientFactory.cs` | Creates `AzureOpenAIClient` from config |
| `Skills/DynamicToolRegistry.cs` | Runtime tool registration by source key |
| `Skills/DynamicToolContextProvider.cs` | Injects dynamic tools into `ChatOptions` |
| `Providers/RagContextProvider.cs` | RAG context injection from vector store |
| `Configuration/AIFoundryOptions.cs` | Options: Endpoint, Key, DeploymentName, etc. |
| `Agents/AgentRegistry.cs` | Builds and caches named agents from persona definitions |
| `Agents/AgentMailbox.cs` | SQLite-backed per-agent message queue (`agentmailbox.db`) |
| `Agents/HandoffToolProvider.cs` | Synchronous agent-to-agent delegation via `handoff_to_agent` |
| `Agents/AgentRegistryToolProvider.cs` | Tools: `list_available_agents`, `get_agent_info` |
| `Agents/AgentMailboxToolProvider.cs` | Tools: `send_to_agent`, `check_inbox`, `reply_to_message` |
| `Agents/InProcessAgentMessaging.cs` | In-process implementation of `IAgentMessaging` for agent-to-agent communication |

**Dependencies:** Abstractions, Microsoft.Agents.AI, Microsoft.Extensions.AI, Azure.AI.OpenAI, Microsoft.Data.Sqlite

### AiAgentCanvas.AgentData

Seven markdown-persisted data domains. Each domain follows an identical pattern: Store + ToolProvider + AIContextProvider. The Goals domain also includes a SQLite-backed work queue for autonomous execution.

Each domain reads from two directories under `agent-data/`: `agent/` (system-written, including seeds and tool-created content) and `user/` (hand-written markdown files, read-only to the system).

| Subdirectory | Domain | Agent Path (system writes) | User Path (read-only) |
|-------------|--------|---------------------------|----------------------|
| `Personas/` | Agent personas with custom instructions | `agent-data/agent/personas/` | `agent-data/user/personas/` |
| `Context/` | Typed persistent context (fact, reference, decision, feedback) | `agent-data/agent/context/` | `agent-data/user/context/` |
| `Workflows/` | Multi-step workflows | `agent-data/agent/workflows/` | `agent-data/user/workflows/` |
| `Entities/` | Domain entities and schemas | `agent-data/agent/entities/` | `agent-data/user/entities/` |
| `Profiles/` | User profiles | `agent-data/agent/profiles/` | `agent-data/user/profiles/` |
| `Guardrails/` | Behavioral guardrails | `agent-data/agent/guardrails/` | `agent-data/user/guardrails/` |
| `Goals/` | Goals and work queue for autonomous execution | `agent-data/agent/goals/` | `agent-data/user/goals/` |

| File | Purpose |
|------|---------|
| `AgentDataServiceExtensions.cs` | Seven `Add*()` methods, one per domain |

**Dependencies:** Core, Abstractions, Microsoft.Agents.AI, Microsoft.Extensions.AI, Microsoft.Data.Sqlite

### AiAgentCanvas.Skills

Skill persistence, MCP connection management, and skill authoring tools.

| File | Purpose |
|------|---------|
| `SkillsServiceExtensions.cs` | `AddAiAgentCanvasSkills()`, `AddAiAgentCanvasMcp()`, `AddAiAgentCanvasSkillRegistry()`, `AddAiAgentCanvasSkillAuthoring()` |
| `SkillStore.cs` | SQLite-backed skill persistence |
| `McpConnectionManager.cs` | Connects to external MCP servers, registers tools |
| `LocalSkillRegistry.cs` | Resolves skills from local files at runtime |
| `SkillAuthoringToolProvider.cs` | Tools for creating/editing skills via chat |
| `SkillToolProvider.cs` | Tools for managing stored skills |

**Dependencies:** Core, ModelContextProtocol, Microsoft.Extensions.AI

### AiAgentCanvas.Security

Microsoft.AgentGovernance integration, prompt injection detection, MCP gateway, and ASP.NET rate limiting.

| File | Purpose |
|------|---------|
| `SecurityServiceExtensions.cs` | `AddAiAgentCanvasSecurity()` and `UseAiAgentCanvasSecurity()` |
| `GovernanceContextProvider.cs` | Scans instructions for prompt injection before LLM calls |
| `GovernedMcpGateway.cs` | Wraps McpGateway for tool allow/deny decisions |
| `GovernedAIFunction.cs` | DelegatingAIFunction that runs governance checks before each tool call |
| `GovernanceToolWrapper.cs` | Implements `IToolGovernanceWrapper` to wrap tools with `GovernedAIFunction` |

**Dependencies:** Abstractions, AgentGovernance, Microsoft.Agents.AI, ASP.NET rate limiting

### AiAgentCanvas.Scheduler

Hangfire-based scheduled task management with SQLite storage. Also includes the autonomous execution engine.

| File | Purpose |
|------|---------|
| `SchedulerServiceExtensions.cs` | `AddAiAgentCanvasScheduler()` |
| `AutonomousAgentJob.cs` | Hangfire recurring job that polls the work queue, claims items, and executes them via `AIAgent.RunAsync` |
| `AutonomousExecutionOptions.cs` | Config: `Enabled`, `MaxIterationsPerRun`, `PollIntervalSeconds`, `CronExpression` |

**Dependencies:** Hangfire, Hangfire.Storage.SQLite, AgentData

### AiAgentCanvas.Notifications

In-memory notification channel using `System.Threading.Channels` with an SSE delivery endpoint.

| File | Purpose |
|------|---------|
| `NotificationServiceExtensions.cs` | `AddAiAgentCanvasNotifications()` |

**Dependencies:** Abstractions

### AiAgentCanvas.SystemTools

Optional file I/O and script execution tools. These are governed by the security layer.

| File | Purpose |
|------|---------|
| `SystemToolsServiceExtensions.cs` | `AddAiAgentCanvasSystemTools()` |

**Dependencies:** Microsoft.Extensions.AI

### AiAgentCanvas.Orchestrator

The composition root (located in `src/Orchestrator/AiAgentCanvas.Orchestrator/`). Contains only `Program.cs` and configuration files. This is the only project that references all other projects.

| File | Purpose |
|------|---------|
| `Program.cs` | Wires all services and middleware |
| `appsettings.json` | AIFoundry, security, and vector store config |
| `governance-policy.yaml` | Default governance rules |

## Agent Projects

The `src/Agents/` folder is where you add all business-specific agent code. Each agent project is a standalone class library that gets wired into the composition root.

### Agent.HelloWorld

A complete starter example demonstrating the custom agent pattern (located in `src/Agents/Agent.HelloWorld/`). Seeds all component types (persona, guardrail, workflow, context, entity, skill) for a financial analyst that references tools from the `MCP.HelloWorldData` data connection. Copy this project as a template for your own agents.

| File | Purpose |
|------|---------|
| `HelloWorldServiceExtensions.cs` | `AddHelloWorldAgent()` seeds persona, guardrail, workflow, context, entity, and skill |

**Dependencies:** Abstractions

## Data Connection Projects

### MCP.HelloWorldData

A sample data connection providing SEC EDGAR and Yahoo Finance tools (located in `src/DataConnections/MCP.HelloWorldData/`). Demonstrates the ToolProvider + ServiceExtensions pattern. Copy this project as a template for your own data connections.

| File | Purpose |
|------|---------|
| `MarketDataToolProvider.cs` | Three AITools: `edgar_company_facts`, `stock_quote`, `stock_history` |
| `MarketDataServiceExtensions.cs` | `AddMarketDataTools()` registers HttpClients and tools |

**Dependencies:** Microsoft.Extensions.AI, Microsoft.Extensions.Http.Resilience

### VectorStore.Sqlite

A SQLite-based vector store used for RAG (Retrieval-Augmented Generation), located in `src/DataConnections/VectorStore.Sqlite/`.

## Adding Your Own Projects

1. Create a new folder under `src/Agents/` (for agents) or `src/DataConnections/` (for tool providers)
2. Add a `.csproj` targeting `net9.0`
3. Implement your agent prompts or tool providers
4. Add a `ProjectReference` in `AiAgentCanvas.Orchestrator.csproj`
5. Add the project to `AiAgentCanvas.sln` under the appropriate solution folder
6. Wire up in `Program.cs`

See [Adding Custom Agents](#adding-custom-agents) and [Adding Custom MCP Connections](#adding-custom-mcp-connections) for detailed walkthroughs.

---

# Core Platform

The `AiAgentCanvas.Core` project is the orchestration engine. It registers the LLM client, manages tools and context providers, and exposes the AG-UI SSE endpoint that CopilotKit connects to.

## ServiceCollectionExtensions

The `AddAiAgentCanvas()` extension method is the central registration point. It wires up the chat client, tool registry, context providers, and the ChatClientAgent.

```csharp
builder.Services.AddAiAgentCanvas(builder.Configuration, options =>
{
    options.AgentName = "AiAgentCanvas";
    options.AgentDescription = "A multi-tool AI assistant with market data, "
        + "scheduling, skills, and MCP integration.";
});
```

### What AddAiAgentCanvas() Registers

1. **AIFoundryOptions** -- binds the `AIFoundry` configuration section
2. **AIFoundryClientFactory** -- singleton that creates `AzureOpenAIClient`
3. **IChatClient** -- wrapped in `ToolDeduplicatingChatClient`, resolved from the factory
4. **DynamicToolRegistry** -- singleton for runtime tool registration
5. **DefaultSystemPrompt** -- wraps the configured or default system prompt
6. **SystemPromptProvider** -- `AIContextProvider` that sets initial instructions
7. **DynamicToolContextProvider** -- `AIContextProvider` that injects dynamic tools
8. **ChatClientAgent** -- the MAF agent with all tools and context providers
9. **CORS** -- allows any origin (intended for local CopilotKit development)

The agent is constructed with all `IReadOnlyList<AITool>` services flattened into `ChatOptions.Tools`, optionally wrapped with governance, and all `AIContextProvider` services passed as the context pipeline:

```csharp
var rawTools = sp.GetServices<IReadOnlyList<AITool>>().SelectMany(t => t).ToList();

// If Security is registered, wrap each tool with governance checks
var governanceWrapper = sp.GetService<IToolGovernanceWrapper>();
var tools = governanceWrapper is not null
    ? rawTools.Select(t => t is AIFunction fn ? (AITool)governanceWrapper.Wrap(fn) : t).ToList()
    : rawTools;

var agentOptions = new ChatClientAgentOptions
{
    Name = options.AgentName,
    Description = options.AgentDescription,
    ChatOptions = new ChatOptions { Tools = tools },
    AIContextProviders = contextProviders,
};

var agent = new ChatClientAgent(chatClient, agentOptions, loggerFactory, sp);
```

### AiAgentCanvasOptions

```csharp
public sealed class AiAgentCanvasOptions
{
    public string AgentName { get; set; } = "AiAgentCanvas";
    public string AgentDescription { get; set; } = "A multi-tool AI assistant";
    public string? SystemPrompt { get; set; }
}
```

### UseAiAgentCanvas()

The `UseAiAgentCanvas()` method configures the middleware pipeline:

```csharp
public static WebApplication UseAiAgentCanvas(this WebApplication app)
{
    app.UseCors();
    app.MapAgUiEndpoints();
    app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));
    return app;
}
```

## AIFoundryClientFactory

Creates the Azure AI Foundry chat client from configuration. Supports both API key and Azure credential authentication.

```csharp
public IChatClient CreateChatClient()
{
    var endpoint = new Uri(_options.Endpoint);
    var inner = _options.UseAzureCredential
        ? new AzureOpenAIClient(endpoint, new DefaultAzureCredential())
        : new AzureOpenAIClient(endpoint, new ApiKeyCredential(_options.Key!));

    return inner.GetChatClient(_options.DeploymentName).AsIChatClient();
}
```

### AIFoundryOptions

Configured in `appsettings.json` under the `AIFoundry` section:

```json
{
  "AIFoundry": {
    "Endpoint": "https://your-resource.services.ai.azure.com/models",
    "Key": "your-api-key",
    "DeploymentName": "gpt-4o",
    "EmbeddingDeploymentName": "text-embedding-3-small",
    "UseAzureCredential": false
  }
}
```

| Property | Required | Description |
|----------|----------|-------------|
| `Endpoint` | Yes | Azure AI Foundry endpoint URL |
| `Key` | Conditional | API key (required when `UseAzureCredential` is false) |
| `DeploymentName` | Yes | Model deployment name |
| `EmbeddingDeploymentName` | No | Embedding model for RAG |
| `UseAzureCredential` | No | Use `DefaultAzureCredential` instead of API key |

## AgUiEndpoint

The AG-UI endpoint handles the CopilotKit frontend protocol. It accepts POST requests at `/api/copilotkit`, extracts chat messages, runs the agent, and streams responses as SSE events.

```csharp
app.MapPost("/api/copilotkit", HandleCopilotKitRequest);
```

### Request Format

CopilotKit sends a JSON body with `messages` and an optional `threadId`:

```json
{
  "threadId": "abc-123",
  "messages": [
    { "role": "user", "content": "What is Apple's latest revenue?" }
  ]
}
```

### SSE Event Sequence

The endpoint streams events in this order:

```
event: run.started
data: {"threadId":"abc-123","runId":"def-456"}

event: text.message.start
data: {"messageId":"ghi-789","role":"assistant","agentName":"AiAgentCanvas"}

event: text.message.content
data: {"messageId":"ghi-789","delta":"Apple's latest"}

event: text.message.content
data: {"messageId":"ghi-789","delta":" quarterly revenue was..."}

event: text.message.end
data: {"messageId":"ghi-789"}

event: run.finished
data: {"threadId":"abc-123","runId":"def-456"}
```

### How It Works

1. Deserializes the request body and extracts messages
2. Maps each message to a `ChatMessage` with the appropriate `ChatRole`
3. Creates a new agent session via `agent.CreateSessionAsync()`
4. Sets the response content type to `text/event-stream`
5. Emits `run.started` and `text.message.start` events
6. Iterates `agent.RunStreamingAsync()` and emits `text.message.content` for each text delta
7. Emits `text.message.end` and `run.finished`

## DynamicToolRegistry

A thread-safe registry that allows tools to be added and removed at runtime. Used primarily by `McpConnectionManager` to register tools from external MCP servers.

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

Tools are keyed by a source string (e.g., `"mcp:my-server"`). When an MCP server connects, its tools are registered under that key. When it disconnects, they are removed.

## DynamicToolContextProvider

An `AIContextProvider` that merges dynamic tools into the chat options before each LLM call. This ensures tools registered at runtime (via MCP connections) are available to the model.

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

## Tool Registration: Two Paths

Tools reach the agent through two mechanisms:

1. **Startup registration** -- Tool providers register `IReadOnlyList<AITool>` as singleton services. These are collected once at startup, optionally wrapped with governance via `IToolGovernanceWrapper`, and passed to `ChatClientAgent` via `ChatOptions.Tools`.
2. **Runtime registration** -- Tools registered into `DynamicToolRegistry` are injected by `DynamicToolContextProvider` before each LLM call. This is used for MCP connections that can come and go.

Both paths result in the LLM seeing all available tools in its context window. When the Security project is registered, every startup tool is wrapped in a `GovernedAIFunction` that runs `GovernedMcpGateway.Evaluate()` before execution and emits audit events for both allowed and blocked calls.

## Context Provider Pipeline

`AIContextProvider` subclasses form a pipeline that runs before each LLM invocation. Each provider can modify the `AIContext` (instructions, tools, or other metadata). The platform registers several providers:

| Provider | Source | Purpose |
|----------|--------|---------|
| `SystemPromptProvider` | Core | Sets the default system prompt |
| `PlanningMiddleware` | Core (middleware) | Goal decomposition: decomposes complex requests into persistent step plans via StateBag |
| `DynamicToolContextProvider` | Core | Injects runtime-registered tools |
| `PersonaContextProvider` | AgentData | Appends active persona instructions |
| `GuardrailContextProvider` | AgentData | Appends guardrail constraints |
| `EntityContextProvider` | AgentData | Appends entity context |
| `PersistentContextProvider` | AgentData | Appends persistent context entries |
| `UserProfileContextProvider` | AgentData | Appends user profile context |
| `GovernanceContextProvider` | Security | Scans for prompt injection |
| `RagContextProvider` | Core | Hybrid search + reranking + cited RAG results |
| `DocumentChunker` | Core | Recursive paragraph/sentence text splitter |
| `LlmReranker` | Core | LLM-based candidate reranking (top-10 to top-3) |

For full details on the RAG pipeline including hybrid search, metadata filtering, reranking, and citation, see the [RAG Pipeline](#rag-pipeline) section.

---

