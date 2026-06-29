# Developer Guide

## Table of Contents

- [Architecture Overview](#architecture-overview)
  - [Request Flow](#request-flow)
  - [Platform vs Custom Separation](#platform-vs-custom-separation)
  - [Dependency Flow](#dependency-flow)
  - [Key Packages](#key-packages)
  - [Extension Method Pattern](#extension-method-pattern)
  - [Inter-Agent Communication](#inter-agent-communication)
  - [Autonomous Execution](#autonomous-execution)
- [Project Structure](#project-structure)
  - [Solution Layout](#solution-layout)
  - [Platform Projects](#platform-projects)
  - [Agent Projects](#agent-projects)
  - [Data Connection Projects](#data-connection-projects)
  - [Adding Your Own Projects](#adding-your-own-projects)
- [Core Platform](#core-platform)
  - [ServiceCollectionExtensions](#servicecollectionextensions)
  - [AIFoundryClientFactory](#aifoundryclientfactory)
  - [AgUiEndpoint](#aguiendpoint)
  - [DynamicToolRegistry](#dynamictoolregistry)
  - [DynamicToolContextProvider](#dynamictoolcontextprovider)
  - [Tool Registration: Two Paths](#tool-registration-two-paths)
  - [Context Provider Pipeline](#context-provider-pipeline)
- [Agent Data](#agent-data)
  - [Directory Layout](#directory-layout)
  - [The Seven Domains](#the-seven-domains)
  - [The Store / ToolProvider / ContextProvider Pattern](#the-store--toolprovider--contextprovider-pattern)
  - [MarkdownFile: The Persistence Foundation](#markdownfile-the-persistence-foundation)
  - [Detailed Walkthrough: Personas](#detailed-walkthrough-personas)
  - [Registration](#registration)
  - [Other Domains](#other-domains)
  - [Context Types](#context-types)
  - [Creating a New Domain](#creating-a-new-domain)
- [Skills & MCP](#skills--mcp)
  - [Overview](#overview)
  - [SkillStore](#skillstore)
  - [McpConnectionManager](#mcpconnectionmanager)
  - [LocalSkillRegistry](#localskillregistry)
  - [SkillAuthoringToolProvider](#skillauthoringtoolprovider)
  - [MCP Connection Lifecycle](#mcp-connection-lifecycle)
  - [Security Integration](#security-integration)
- [RAG Pipeline](#rag-pipeline)
  - [Pipeline Overview](#pipeline-overview)
  - [Document Chunking](#document-chunking)
  - [Hybrid Search](#hybrid-search)
  - [Metadata Filtering](#metadata-filtering)
  - [LLM-Based Reranking](#llm-based-reranking)
  - [Citation and Attribution](#citation-and-attribution)
  - [DocumentRecord Schema](#documentrecord-schema)
  - [Service Registration](#service-registration)
  - [Ingestion Example](#ingestion-example)
  - [Architecture Summary](#architecture-summary)
- [Security](#security)
  - [Security Overview](#security-overview)
  - [AddAiAgentCanvasSecurity()](#addaiagentcanvassecurity)
  - [GovernanceKernel](#governancekernel)
  - [GovernanceContextProvider](#governancecontextprovider)
  - [GovernedMcpGateway](#governedmcpgateway)
  - [Tool-Call Governance](#tool-call-governance)
  - [Governance Policy (YAML)](#governance-policy-yaml)
  - [ASP.NET Rate Limiting](#aspnet-rate-limiting)
  - [Security Headers](#security-headers)
  - [Customizing Security](#customizing-security)
- [Adding Custom Agents](#adding-custom-agents)
  - [How It Works](#how-it-works)
  - [Step-by-Step Guide](#step-by-step-guide)
  - [Full Reference: HelloWorldAgent](#full-reference-helloworldagent)
  - [Component Seeding](#component-seeding)
  - [Switching Between Agents](#switching-between-agents)
  - [Testing Your Agent](#testing-your-agent)
- [Adding Custom MCP Connections](#adding-custom-mcp-connections)
  - [How Tool Registration Works](#how-tool-registration-works)
  - [Step-by-Step Guide (MCP)](#step-by-step-guide-mcp)
  - [Full Reference: MCP.HelloWorldData](#full-reference-mcphelloworlddata)
  - [Testing Tools](#testing-tools)
  - [Tool Design Guidelines](#tool-design-guidelines)
  - [Tools Without External APIs](#tools-without-external-apis)

---

## Architecture Overview

AI Agent Canvas is a .NET multi-agent copilot platform built on Microsoft's Agent Framework (MAF) and Microsoft.Extensions.AI. It connects a CopilotKit frontend to Azure AI Foundry models through an AG-UI Server-Sent Events protocol, with dynamic tool registration, governance, and markdown-persisted agent data.

### Request Flow

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

### Platform vs Custom Separation

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

### Dependency Flow

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

### Key Packages

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.Agents.AI | 1.10.0 | ChatClientAgent, AIContextProvider, AIAgent |
| Microsoft.Extensions.AI | 10.7.0 | IChatClient, AITool, AIFunctionFactory, ChatOptions |
| ModelContextProtocol | 1.4.0 | McpClient, HttpClientTransport for MCP servers |
| Microsoft.AgentGovernance | 4.0.0 | GovernanceKernel, PolicyEngine, InjectionDetector |
| Azure.AI.OpenAI | 2.3.0-beta.1 | AzureOpenAIClient for Azure AI Foundry |
| Hangfire | 1.8.23 | Background job scheduling with SQLite storage |

### Extension Method Pattern

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

### Inter-Agent Communication

AI Agent Canvas supports multi-agent collaboration through three mechanisms registered via `AddAiAgentCanvasInterAgentCommunication()` in Core:

- **Agent Registry** -- Builds and caches named `ChatClientAgent` instances from persona definitions. Each persona becomes a resolvable agent with its own instructions. Tools: `list_available_agents`, `get_agent_info`.
- **Agent Mailbox** -- SQLite-backed per-agent message queue (`agentmailbox.db`) for asynchronous communication between agents. Tools: `send_to_agent`, `check_inbox`, `reply_to_message`.
- **Handoff** -- Synchronous delegation: one agent calls `handoff_to_agent` to run a target agent and get the result back in the same turn.

### Autonomous Execution

The Scheduler project includes an autonomous execution mode built on Hangfire. When enabled, a recurring `AutonomousAgentJob` polls the work queue for pending items, claims and executes them via `AIAgent.RunAsync`, and falls back to picking the next active goal when the queue is empty. Goals and work items are managed through the AgentData Goals domain.

Tools: `start_autonomous_mode`, `stop_autonomous_mode`, `get_autonomous_status` (on the Scheduler), plus `create_goal`, `list_goals`, `submit_work_item`, etc. (on AgentData).

---

## Project Structure

This page describes every project in the AI Agent Canvas solution, its purpose, key files, and dependencies.

### Solution Layout

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

### Platform Projects

#### AiAgentCanvas.Abstractions

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

#### AiAgentCanvas.Core

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

#### AiAgentCanvas.AgentData

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

#### AiAgentCanvas.Skills

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

#### AiAgentCanvas.Security

Microsoft.AgentGovernance integration, prompt injection detection, MCP gateway, and ASP.NET rate limiting.

| File | Purpose |
|------|---------|
| `SecurityServiceExtensions.cs` | `AddAiAgentCanvasSecurity()` and `UseAiAgentCanvasSecurity()` |
| `GovernanceContextProvider.cs` | Scans instructions for prompt injection before LLM calls |
| `GovernedMcpGateway.cs` | Wraps McpGateway for tool allow/deny decisions |
| `GovernedAIFunction.cs` | DelegatingAIFunction that runs governance checks before each tool call |
| `GovernanceToolWrapper.cs` | Implements `IToolGovernanceWrapper` to wrap tools with `GovernedAIFunction` |

**Dependencies:** Abstractions, AgentGovernance, Microsoft.Agents.AI, ASP.NET rate limiting

#### AiAgentCanvas.Scheduler

Hangfire-based scheduled task management with SQLite storage. Also includes the autonomous execution engine.

| File | Purpose |
|------|---------|
| `SchedulerServiceExtensions.cs` | `AddAiAgentCanvasScheduler()` |
| `AutonomousAgentJob.cs` | Hangfire recurring job that polls the work queue, claims items, and executes them via `AIAgent.RunAsync` |
| `AutonomousExecutionOptions.cs` | Config: `Enabled`, `MaxIterationsPerRun`, `PollIntervalSeconds`, `CronExpression` |

**Dependencies:** Hangfire, Hangfire.Storage.SQLite, AgentData

#### AiAgentCanvas.Notifications

In-memory notification channel using `System.Threading.Channels` with an SSE delivery endpoint.

| File | Purpose |
|------|---------|
| `NotificationServiceExtensions.cs` | `AddAiAgentCanvasNotifications()` |

**Dependencies:** Abstractions

#### AiAgentCanvas.SystemTools

Optional file I/O and script execution tools. These are governed by the security layer.

| File | Purpose |
|------|---------|
| `SystemToolsServiceExtensions.cs` | `AddAiAgentCanvasSystemTools()` |

**Dependencies:** Microsoft.Extensions.AI

#### AiAgentCanvas.Orchestrator

The composition root (located in `src/Orchestrator/AiAgentCanvas.Orchestrator/`). Contains only `Program.cs` and configuration files. This is the only project that references all other projects.

| File | Purpose |
|------|---------|
| `Program.cs` | Wires all services and middleware |
| `appsettings.json` | AIFoundry, security, and vector store config |
| `governance-policy.yaml` | Default governance rules |

### Agent Projects

The `src/Agents/` folder is where you add all business-specific agent code. Each agent project is a standalone class library that gets wired into the composition root.

#### Agent.HelloWorld

A complete starter example demonstrating the custom agent pattern (located in `src/Agents/Agent.HelloWorld/`). Seeds all component types (persona, guardrail, workflow, context, entity, skill) for a financial analyst that references tools from the `MCP.HelloWorldData` data connection. Copy this project as a template for your own agents.

| File | Purpose |
|------|---------|
| `HelloWorldServiceExtensions.cs` | `AddHelloWorldAgent()` seeds persona, guardrail, workflow, context, entity, and skill |

**Dependencies:** Abstractions

### Data Connection Projects

#### MCP.HelloWorldData

A sample data connection providing SEC EDGAR and Yahoo Finance tools (located in `src/DataConnections/MCP.HelloWorldData/`). Demonstrates the ToolProvider + ServiceExtensions pattern. Copy this project as a template for your own data connections.

| File | Purpose |
|------|---------|
| `MarketDataToolProvider.cs` | Three AITools: `edgar_company_facts`, `stock_quote`, `stock_history` |
| `MarketDataServiceExtensions.cs` | `AddMarketDataTools()` registers HttpClients and tools |

**Dependencies:** Microsoft.Extensions.AI, Microsoft.Extensions.Http.Resilience

#### VectorStore.Sqlite

A SQLite-based vector store used for RAG (Retrieval-Augmented Generation), located in `src/DataConnections/VectorStore.Sqlite/`.

### Adding Your Own Projects

1. Create a new folder under `src/Agents/` (for agents) or `src/DataConnections/` (for tool providers)
2. Add a `.csproj` targeting `net9.0`
3. Implement your agent prompts or tool providers
4. Add a `ProjectReference` in `AiAgentCanvas.Orchestrator.csproj`
5. Add the project to `AiAgentCanvas.sln` under the appropriate solution folder
6. Wire up in `Program.cs`

See [Adding Custom Agents](#adding-custom-agents) and [Adding Custom MCP Connections](#adding-custom-mcp-connections) for detailed walkthroughs.

---

## Core Platform

The `AiAgentCanvas.Core` project is the orchestration engine. It registers the LLM client, manages tools and context providers, and exposes the AG-UI SSE endpoint that CopilotKit connects to.

### ServiceCollectionExtensions

The `AddAiAgentCanvas()` extension method is the central registration point. It wires up the chat client, tool registry, context providers, and the ChatClientAgent.

```csharp
builder.Services.AddAiAgentCanvas(builder.Configuration, options =>
{
    options.AgentName = "AiAgentCanvas";
    options.AgentDescription = "A multi-tool AI assistant with market data, "
        + "scheduling, skills, and MCP integration.";
});
```

#### What AddAiAgentCanvas() Registers

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

#### AiAgentCanvasOptions

```csharp
public sealed class AiAgentCanvasOptions
{
    public string AgentName { get; set; } = "AiAgentCanvas";
    public string AgentDescription { get; set; } = "A multi-tool AI assistant";
    public string? SystemPrompt { get; set; }
}
```

#### UseAiAgentCanvas()

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

### AIFoundryClientFactory

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

#### AIFoundryOptions

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

### AgUiEndpoint

The AG-UI endpoint handles the CopilotKit frontend protocol. It accepts POST requests at `/api/copilotkit`, extracts chat messages, runs the agent, and streams responses as SSE events.

```csharp
app.MapPost("/api/copilotkit", HandleCopilotKitRequest);
```

#### Request Format

CopilotKit sends a JSON body with `messages` and an optional `threadId`:

```json
{
  "threadId": "abc-123",
  "messages": [
    { "role": "user", "content": "What is Apple's latest revenue?" }
  ]
}
```

#### SSE Event Sequence

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

#### How It Works

1. Deserializes the request body and extracts messages
2. Maps each message to a `ChatMessage` with the appropriate `ChatRole`
3. Creates a new agent session via `agent.CreateSessionAsync()`
4. Sets the response content type to `text/event-stream`
5. Emits `run.started` and `text.message.start` events
6. Iterates `agent.RunStreamingAsync()` and emits `text.message.content` for each text delta
7. Emits `text.message.end` and `run.finished`

### DynamicToolRegistry

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

### DynamicToolContextProvider

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

### Tool Registration: Two Paths

Tools reach the agent through two mechanisms:

1. **Startup registration** -- Tool providers register `IReadOnlyList<AITool>` as singleton services. These are collected once at startup, optionally wrapped with governance via `IToolGovernanceWrapper`, and passed to `ChatClientAgent` via `ChatOptions.Tools`.
2. **Runtime registration** -- Tools registered into `DynamicToolRegistry` are injected by `DynamicToolContextProvider` before each LLM call. This is used for MCP connections that can come and go.

Both paths result in the LLM seeing all available tools in its context window. When the Security project is registered, every startup tool is wrapped in a `GovernedAIFunction` that runs `GovernedMcpGateway.Evaluate()` before execution and emits audit events for both allowed and blocked calls.

### Context Provider Pipeline

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

## Agent Data

The `AiAgentCanvas.AgentData` project provides seven markdown-persisted data domains. Each domain follows an identical three-class pattern and gives the LLM tools to manage its own configuration at runtime.

### Directory Layout

Agent data defaults to per-agent directories under a common `agent-data/` root. The orchestrator and each agent get their own subdirectory, with a `shared/` directory for cross-agent data. Each subdirectory contains `agent/` (system writes) and `user/` (hand-written, read-only to system) folders:

```
agent-data/
├── orchestrator/       <-- Orchestrator's own agent data
│   ├── agent/          <-- System writes here (seeds + tool-created content)
│   │   ├── personas/
│   │   ├── context/
│   │   ├── workflows/
│   │   ├── entities/
│   │   ├── profiles/
│   │   ├── guardrails/
│   │   └── goals/
│   └── user/           <-- Hand-written MD files (read-only to system, never overwritten)
│       ├── personas/
│       ├── context/
│       └── ...
└── shared/             <-- Shared data accessible to all agents
    ├── agent/
    │   ├── personas/
    │   ├── context/
    │   └── ...
    └── user/
        ├── personas/
        ├── context/
        └── ...
```

- **`agent/`** -- All system writes go here: seed data from custom agents and content created via chat tools (create_persona, save_context, etc.).
- **`user/`** -- A place for hand-written markdown files that the system reads but never overwrites. This is the no-code alternative to implementing seed interfaces -- simply drop a properly formatted markdown file into the appropriate subdirectory.
- **`shared/`** -- Data that is accessible to all agents. Use the `sharedRootDirectory` parameter when registering domains to enable shared data alongside per-agent data.

All directories are created automatically on startup. Each store's `ListAll()` method merges files from both the per-agent and shared directories, so the LLM sees user-provided, system-created, and shared data together.

### The Seven Domains

| Domain | Agent Path | User Path | Purpose |
|--------|-----------|-----------|---------|
| Personas | `agent/personas/` | `user/personas/` | Custom agent identities with instructions |
| Context | `agent/context/` | `user/context/` | Typed persistent context (fact, reference, decision, feedback) |
| Workflows | `agent/workflows/` | `user/workflows/` | Multi-step workflow definitions |
| Entities | `agent/entities/` | `user/entities/` | Domain entity schemas |
| UserProfiles | `agent/profiles/` | `user/profiles/` | User preference profiles |
| Guardrails | `agent/guardrails/` | `user/guardrails/` | Behavioral constraints |
| Goals | `agent/goals/` | `user/goals/` | Goals and objectives for autonomous execution |

### The Store / ToolProvider / ContextProvider Pattern

Every domain implements exactly three classes:

1. **Store** -- CRUD operations on markdown files in a directory
2. **ToolProvider** -- Exposes store operations as `AITool` instances the LLM can call
3. **AIContextProvider** (optional) -- Injects domain data into the system prompt before each LLM call

This pattern means the LLM can create, read, update, and delete domain data through tool calls, and that data automatically influences the LLM's behavior through context injection.

### MarkdownFile: The Persistence Foundation

All agent data is stored as markdown files with YAML frontmatter. The `MarkdownFile` utility class in `AiAgentCanvas.Abstractions` handles parsing and serialization.

#### File Format

```markdown
---
name: code-reviewer
description: Reviews code for quality and correctness
---

You are an expert code reviewer. When reviewing code:
- Check for bugs, security issues, and performance problems
- Suggest improvements with specific code examples
- Be constructive and explain your reasoning
```

#### Key Methods

```csharp
// Parse a single file
MarkdownFile? file = MarkdownFile.Parse("agent-data/personas/code-reviewer.md");
string name = file.Get("name");           // "code-reviewer"
string desc = file.Get("description");    // "Reviews code for quality..."
string body = file.Body;                  // The instruction text

// Write a file (creates directory if needed)
MarkdownFile.Write(
    "agent-data/personas/code-reviewer.md",
    new Dictionary<string, string>
    {
        ["name"] = "code-reviewer",
        ["description"] = "Reviews code for quality and correctness",
    },
    "You are an expert code reviewer...");

// Load all files from a directory
List<MarkdownFile> all = MarkdownFile.LoadAll("agent-data/personas/");

// Sanitize a name for use as a filename
string safe = MarkdownFile.SanitizeFileName("Code Reviewer"); // "code-reviewer"
```

### Detailed Walkthrough: Personas

Personas are the most illustrative domain. Here is how all three classes work together.

#### PersonaStore

The store manages CRUD operations on persona markdown files and tracks which persona is active via a `.active` marker file.

```csharp
public sealed class PersonaStore
{
    private readonly string _directory;        // agent/ -- system writes here
    private readonly string _userDirectory;    // user/ -- read-only, never overwritten
    private readonly string[] _readDirectories;

    public PersonaStore(string directory, string userDirectory)
    {
        // Creates both directories if missing
    }

    public void SavePersona(string name, string description, string instructions)
    {
        // Always writes to _directory (agent/)
        MarkdownFile.Write(
            Path.Combine(_directory, MarkdownFile.SanitizeFileName(name) + ".md"),
            new Dictionary<string, string>
            {
                ["name"] = name,
                ["description"] = description,
            },
            instructions);
    }

    public List<PersonaInfo> ListPersonas()
    {
        // Merges files from both agent/ and user/ directories
        return _readDirectories
            .SelectMany(dir => MarkdownFile.LoadAll(dir))
            .Select(ToPersona)
            .Where(p => p is not null)
            .Cast<PersonaInfo>()
            .ToList();
    }

    public PersonaInfo? GetPersona(string name) { ... }
    public bool DeletePersona(string name) { ... }
    public string? GetActivePersonaName() { ... }
    public void SetActivePersona(string? name) { ... }
    public string? GetActiveInstructions() { ... }
}
```

#### PersonaToolProvider

The tool provider creates `AITool` instances using `AIFunctionFactory.Create()`. Each tool delegates to the store.

```csharp
public sealed class PersonaToolProvider
{
    private readonly PersonaStore _store;

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(CreatePersona, "create_persona",
                "Create a new persona with custom instructions"),
            AIFunctionFactory.Create(ListPersonas, "list_personas",
                "List all available personas"),
            AIFunctionFactory.Create(SwitchPersona, "switch_persona",
                "Switch to a different persona"),
            AIFunctionFactory.Create(ReadPersona, "read_persona",
                "Read the full details of a persona"),
            AIFunctionFactory.Create(UpdatePersona, "update_persona",
                "Update an existing persona"),
            AIFunctionFactory.Create(DeletePersona, "delete_persona",
                "Delete a persona"),
        ];
    }

    private string CreatePersona(string name, string description, string instructions)
    {
        var existing = _store.GetPersona(name);
        if (existing is not null)
            return JsonSerializer.Serialize(new { error = "Persona already exists..." });

        _store.SavePersona(name, description, instructions);
        return JsonSerializer.Serialize(new { status = "created", name });
    }

    // ... similar implementations for other tools
}
```

#### PersonaContextProvider

The context provider reads the active persona and appends its instructions to the system prompt before each LLM call.

```csharp
internal sealed class PersonaContextProvider : AIContextProvider
{
    private readonly PersonaStore _store;
    private readonly string _defaultPrompt;

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        var activeInstructions = _store.GetActiveInstructions();
        if (!string.IsNullOrEmpty(activeInstructions))
        {
            context.AIContext.Instructions =
                (context.AIContext.Instructions ?? "") + "\n" + activeInstructions;
        }
        else if (string.IsNullOrEmpty(context.AIContext.Instructions))
        {
            context.AIContext.Instructions = _defaultPrompt;
        }
        return new ValueTask<AIContext>(context.AIContext);
    }
}
```

### Registration

The `AgentDataServiceExtensions` class provides one `Add*()` method per domain. Each method registers the Store, ToolProvider, tools, and (where applicable) the AIContextProvider.

```csharp
private const string DefaultRoot = "./agent-data/orchestrator";
private const string DefaultSharedRoot = "./agent-data/shared";

public static IServiceCollection AddAiAgentCanvasPersonas(
    this IServiceCollection services,
    string rootDirectory = DefaultRoot,
    string? sharedRootDirectory = DefaultSharedRoot)
{
    var store = new PersonaStore(
        Path.Combine(rootDirectory, "agent", "personas"),   // system writes
        Path.Combine(rootDirectory, "user", "personas"),    // user-provided (read-only)
        sharedRootDirectory is not null
            ? Path.Combine(sharedRootDirectory, "agent", "personas")
            : null);                                        // shared data (optional)
    services.AddSingleton(store);
    services.AddSingleton<PersonaToolProvider>();
    services.AddSingleton<IReadOnlyList<AITool>>(sp =>
        sp.GetRequiredService<PersonaToolProvider>().GetTools());
    services.AddSingleton<AIContextProvider>(sp =>
    {
        // Seed data from custom agents is written to agent/ directory
        foreach (var seed in sp.GetServices<IPersonaSeed>())
        {
            if (store.GetPersona(seed.Name) is null)
                store.SavePersona(seed.Name, seed.Description, seed.Instructions);
        }
        var defaultPrompt = sp.GetRequiredService<DefaultSystemPrompt>().Value;
        return new PersonaContextProvider(store, defaultPrompt);
    });
    return services;
}
```

All seven domains are registered individually in `Program.cs`:

```csharp
builder.Services.AddAiAgentCanvasPersonas();
builder.Services.AddAiAgentCanvasContext();
builder.Services.AddAiAgentCanvasWorkflows();
builder.Services.AddAiAgentCanvasEntities();
builder.Services.AddAiAgentCanvasUserProfiles();
builder.Services.AddAiAgentCanvasGuardrails();
builder.Services.AddAiAgentCanvasGoals();
```

### Other Domains

Each domain follows the same pattern as Personas but with domain-specific data:

| Domain | Context Provider | What it injects |
|--------|-----------------|-----------------|
| Personas | `PersonaContextProvider` | Active persona instructions |
| Context | `PersistentContextProvider` | All persistent context entries, grouped by type (fact, reference, decision, feedback) |
| Guardrails | `GuardrailContextProvider` | Active guardrail constraints |
| Entities | `EntityContextProvider` | Entity schemas and definitions |
| UserProfiles | `UserProfileContextProvider` | Active user preferences |
| Workflows | *(none)* | Executed on demand via `WorkflowExecutor` |
| Goals | *(none)* | Goals are managed via tools only; used by the autonomous execution job |

### Context Types

Context entries support an optional `type` field in their YAML frontmatter that categorizes the knowledge. When injected into the system prompt, entries are grouped by type for clarity.

| Type | Purpose | Example |
|------|---------|---------|
| `fact` | Domain knowledge -- things that are true | "Our fiscal year starts in April", "The main database is Postgres 16" |
| `reference` | Pointers to external systems and resources | "Bug tracker is at linear.app/project-X", "Oncall dashboard at grafana.internal/api-latency" |
| `decision` | Past choices with rationale | "Chose GraphQL over REST because clients need flexible queries" |
| `feedback` | Learned behavioral adjustments | "User prefers tables over bullet points", "Always include source citations" |

The type field is free-form -- you can use any string, not just the four conventions above. Entries without a type are grouped as "General". When injected into the system prompt, entries are grouped by their type label.

#### File Format

```markdown
---
topic: fiscal-year-schedule
type: fact
tags: finance,calendar
---
Our fiscal year runs April 1 through March 31.
Q1: Apr-Jun, Q2: Jul-Sep, Q3: Oct-Dec, Q4: Jan-Mar.
```

### Creating a New Domain

To add a new agent data domain, follow the same pattern:

1. Create a subdirectory under `AiAgentCanvas.AgentData/` (e.g., `Templates/`)
2. Create a `TemplateStore` class with CRUD methods using `MarkdownFile`
3. Create a `TemplateToolProvider` class exposing tools via `AIFunctionFactory.Create()`
4. Optionally create a `TemplateContextProvider` extending `AIContextProvider`
5. Add an `AddAiAgentCanvasTemplates()` method to `AgentDataServiceExtensions`
6. Call the new method in `Program.cs`

---

## Skills & MCP

The `AiAgentCanvas.Skills` project manages skill persistence, external MCP server connections, local skill resolution, and skill authoring. It enables the agent to connect to arbitrary MCP servers at runtime and register their tools dynamically.

### Overview

The skills system has four components, each with its own registration method:

| Component | Registration | Purpose |
|-----------|-------------|---------|
| `SkillStore` + `SkillToolProvider` | `AddAiAgentCanvasSkills()` | SQLite-backed skill persistence and management |
| `McpConnectionManager` | `AddAiAgentCanvasMcp()` | Connect/disconnect external MCP servers at runtime |
| `LocalSkillRegistry` | `AddAiAgentCanvasSkillRegistry()` | Resolve skills from local markdown files |
| `SkillAuthoringToolProvider` | `AddAiAgentCanvasSkillAuthoring()` | Create and edit skills via chat |

```csharp
builder.Services.AddAiAgentCanvasSkills();
builder.Services.AddAiAgentCanvasMcp();
builder.Services.AddAiAgentCanvasSkillRegistry();
builder.Services.AddAiAgentCanvasSkillAuthoring();
```

### SkillStore

The `SkillStore` persists skill definitions in a SQLite database. It provides CRUD operations for skill records.

```csharp
public sealed class SkillStore
{
    public SkillStore(string connectionString = "Data Source=skills.db") { ... }
}
```

The `SkillToolProvider` exposes these operations as AITools so the LLM can manage skills through conversation.

#### Registration

```csharp
public static IServiceCollection AddAiAgentCanvasSkills(
    this IServiceCollection services,
    string connectionString = "Data Source=skills.db")
{
    services.AddSingleton(new SkillStore(connectionString));
    services.AddSingleton<SkillToolProvider>();
    services.AddSingleton<IReadOnlyList<AITool>>(sp =>
        sp.GetRequiredService<SkillToolProvider>().GetTools());
    return services;
}
```

### McpConnectionManager

The `McpConnectionManager` is the runtime MCP client. It allows the agent to connect to external MCP servers, discover their tools, and register those tools into the `DynamicToolRegistry` so the LLM can use them.

#### How It Works

1. The LLM calls the `connect_mcp_server` tool with a name, endpoint URL, and transport type
2. `McpConnectionManager` creates an `HttpClientTransport` and connects via `McpClient.CreateAsync()`
3. It calls `ListToolsAsync()` to discover the server's tools
4. The discovered tools are registered into `DynamicToolRegistry` under the key `mcp:{name}`
5. `DynamicToolContextProvider` picks up the new tools on the next LLM invocation

#### Exposed Tools

The manager itself exposes three management tools:

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

#### connect_mcp_server

Connects to an MCP server and registers its tools:

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
    var aiTools = mcpTools.Cast<AITool>().ToList();

    _connections[name] = new McpConnection { /* ... */ };
    _registry.Register($"mcp:{name}", aiTools);

    return JsonSerializer.Serialize(new {
        status = "connected", name, endpoint,
        toolCount = aiTools.Count,
        tools = aiTools.Select(t => t.Name).ToList()
    });
}
```

#### disconnect_mcp_server

Removes a connection and unregisters its tools from the `DynamicToolRegistry`:

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

#### list_mcp_connections

Returns all active connections with their tool counts:

```csharp
private string ListMcpConnections()
{
    var connections = _connections.Values.Select(c => new
    {
        c.Name, c.Endpoint, c.Transport,
        toolCount = c.Tools?.Count ?? 0,
    }).ToList();

    return JsonSerializer.Serialize(new { count = connections.Count, connections });
}
```

#### Registration

```csharp
public static IServiceCollection AddAiAgentCanvasMcp(this IServiceCollection services)
{
    services.AddSingleton<McpConnectionManager>();
    services.AddSingleton<IReadOnlyList<AITool>>(sp =>
        sp.GetRequiredService<McpConnectionManager>().GetTools());
    return services;
}
```

### LocalSkillRegistry

The `LocalSkillRegistry` resolves skills from local markdown files at runtime. It combines data from the `SkillStore`, `McpConnectionManager`, and skill files on disk.

```csharp
public static IServiceCollection AddAiAgentCanvasSkillRegistry(
    this IServiceCollection services,
    string skillsDirectory = "./agent-data/skills")
{
    services.AddSingleton(sp => new LocalSkillRegistry(
        skillsDirectory,
        sp.GetRequiredService<SkillStore>(),
        sp.GetRequiredService<McpConnectionManager>(),
        sp.GetRequiredService<ILogger<LocalSkillRegistry>>()));
    services.AddSingleton<IReadOnlyList<AITool>>(sp =>
        sp.GetRequiredService<LocalSkillRegistry>().GetTools());
    return services;
}
```

### SkillAuthoringToolProvider

Provides tools for creating and editing skill definitions through conversation. Skills are stored as markdown files in the skills directory.

```csharp
public static IServiceCollection AddAiAgentCanvasSkillAuthoring(
    this IServiceCollection services,
    string skillsDirectory = "./agent-data/skills")
{
    services.AddSingleton(sp => new SkillAuthoringToolProvider(
        skillsDirectory,
        sp.GetRequiredService<ILogger<SkillAuthoringToolProvider>>()));
    services.AddSingleton<IReadOnlyList<AITool>>(sp =>
        sp.GetRequiredService<SkillAuthoringToolProvider>().GetTools());
    return services;
}
```

### MCP Connection Lifecycle

```
User: "Connect to my data server at https://data.example.com/mcp"
    |
    v
LLM calls connect_mcp_server(name="data", endpoint="https://...", transport="http")
    |
    v
McpConnectionManager:
  1. Creates HttpClientTransport
  2. Connects via McpClient.CreateAsync()
  3. Lists tools via ListToolsAsync()
  4. Registers tools: _registry.Register("mcp:data", aiTools)
    |
    v
Next LLM invocation:
  DynamicToolContextProvider reads DynamicToolRegistry
  -> MCP server's tools appear in ChatOptions.Tools
  -> LLM can now call them
    |
    v
User: "Disconnect from the data server"
    |
    v
LLM calls disconnect_mcp_server(name="data")
    |
    v
McpConnectionManager:
  1. _registry.Unregister("mcp:data")
  2. Disposes McpClient
  -> Tools removed from future invocations
```

### Security Integration

MCP connections are subject to governance policies. The `GovernedMcpGateway` in the Security project can block connections to private/internal addresses (SSRF protection) and require approval for specific tools. See the [Security](#security) section for details on the governance policy rules that apply to `connect_mcp_server`.

---

## RAG Pipeline

AI Agent Canvas includes a full Retrieval-Augmented Generation (RAG) pipeline that enriches every agent response with relevant documents from a vector store. The pipeline goes beyond basic vector search with **recursive chunking**, **hybrid search** (vector + keyword), **metadata filtering**, **LLM-based reranking**, and **citation attribution**.

### Pipeline Overview

```
Document Ingestion                          Query Pipeline
================                          ==============

Raw Document                               User Message
    |                                          |
    v                                          v
DocumentChunker                            Embed Query
(recursive split by                        (IEmbeddingGenerator)
 paragraph -> sentence)                        |
    |                                          v
    v                                     Hybrid Search
Embed Each Chunk                          (vector cosine similarity
(IEmbeddingGenerator)                      + FTS5 keyword/BM25)
    |                                          |
    v                                          v
Store in SQLite                           Metadata Filtering
(vector BLOB + FTS5                       (source, tags)
 full-text index)                              |
                                               v
                                          LLM Reranking
                                          (top-10 -> top-3)
                                               |
                                               v
                                          Citation Formatting
                                          ([1] source: X, score: 0.82)
                                               |
                                               v
                                          Inject into Agent Context
```

### Document Chunking

The `DocumentChunker` service in `AiAgentCanvas.Core` recursively splits documents into chunks suitable for embedding. Rather than using fixed-size character windows, it follows a semantic hierarchy:

1. **Split by paragraphs** first (double newlines)
2. If a paragraph exceeds `ChunkSize` (default 512 chars), **split by sentences** (period/exclamation/question followed by space or newline)
3. Apply **overlap** (default 64 chars) between consecutive chunks for context continuity
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

Each chunk is returned as a `DocumentChunk` with the text, a sequential index, and an optional source identifier. The source flows through to the `DocumentRecord` for citation tracking.

#### Configuration

| Property | Default | Description |
|----------|---------|-------------|
| `ChunkSize` | 512 | Maximum characters per chunk |
| `ChunkOverlap` | 64 | Characters of overlap between consecutive chunks |

### Hybrid Search

The SQLite vector store supports **hybrid search** that combines two retrieval signals:

- **Vector search** (cosine similarity) -- captures semantic meaning
- **Keyword search** (SQLite FTS5 / BM25) -- captures exact term matches

At ingestion time, each document is stored in both the main table (with the embedding BLOB) and an FTS5 full-text index. At query time, both scores are computed and combined:

```
finalScore = (vectorWeight * cosineSimilarity) + (keywordWeight * bm25Score)
```

Default weights are **70% vector, 30% keyword**. This means semantic similarity dominates, but exact keyword matches get a meaningful boost -- particularly useful when users search for specific terms, product names, or technical jargon that embedding models may not represent precisely.

#### FTS5 Integration

The FTS5 virtual table is created alongside the main documents table:

```sql
CREATE VIRTUAL TABLE IF NOT EXISTS [documents_fts]
USING fts5(id UNINDEXED, text, content=[documents], content_rowid=rowid)
```

Query terms are sanitized and joined with OR for broad matching. The BM25 rank is normalized to a 0-1 scale using `1 / (1 + |rank|)`.

#### IHybridSearchable Interface

The hybrid search capability is exposed through `IHybridSearchable` in Abstractions, keeping Core decoupled from the SQLite implementation:

```csharp
public interface IHybridSearchable
{
    IAsyncEnumerable<(DocumentRecord Record, double? Score)> HybridSearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int top,
        RagSearchOptions? options = null,
        CancellationToken ct = default);
}
```

The `RagContextProvider` checks if the collection implements `IHybridSearchable` and uses it when available, falling back to standard vector-only search otherwise.

### Metadata Filtering

`DocumentRecord` now includes two filterable fields:

| Field | Type | Filter | Example |
|-------|------|--------|---------|
| `Source` | string? | Exact match | `"annual-report-2024.pdf"` |
| `Tags` | string? | Contains (LIKE) | `"finance,quarterly,earnings"` |

Filters are applied as SQL WHERE clauses **before** vector scoring, reducing the candidate set and improving both relevance and performance:

```csharp
var options = new RagSearchOptions
{
    SourceFilter = "annual-report-2024.pdf",  // exact match
    TagFilter = "finance",                     // LIKE '%finance%'
    KeywordQuery = "revenue breakdown",        // FTS5 keyword boost
    VectorWeight = 0.7f,
    KeywordWeight = 0.3f,
};
```

### LLM-Based Reranking

After retrieval, the `LlmReranker` uses the same LLM to re-score candidates by relevance. This is a **cross-encoder pattern** implemented via prompting -- the LLM sees both the query and each candidate together, which produces better relevance judgments than embedding similarity alone.

#### How It Works

1. Retrieve top-10 candidates via hybrid search
2. Send the query + all 10 candidates (truncated to 300 chars each) to the LLM
3. Ask the LLM to return a JSON array ranking the candidates by relevance
4. Take the top-3 from the reranked list
5. If the LLM call fails, fall back to the original ranking

```csharp
public async Task<List<VectorSearchResult<DocumentRecord>>> RerankAsync(
    string query,
    List<VectorSearchResult<DocumentRecord>> candidates,
    int topN = 3,
    CancellationToken ct = default)
{
    if (candidates.Count <= topN)
        return candidates;

    // Build prompt: "Given query X, rank these chunks [0]-[9] by relevance"
    // Parse JSON response: [2, 0, 7, ...] -> reorder candidates
    // Fallback: return candidates.Take(topN) on any error
}
```

#### Cost Considerations

Reranking adds one extra LLM call per user query. The call uses `MaxOutputTokens=100` and `Temperature=0` to keep it fast and deterministic. For latency-sensitive deployments, the reranker is optional -- remove it from DI registration to skip this step.

### Citation and Attribution

The `RagContextProvider` formats each retrieved chunk with a numbered citation that includes the source and relevance score:

```
Relevant context from documents (cite by number when using):

[1] (source: annual-report-2024.pdf, score: 0.892)
Revenue increased 12% year-over-year to $4.2B, driven by cloud services
growth in the enterprise segment...

---

[2] (source: earnings-call-q4.pdf, score: 0.847)
Management highlighted three strategic priorities for the coming fiscal
year: AI infrastructure, developer tooling, and...

---

[3] (source: market-analysis.pdf, score: 0.791)
Industry analysts project 15-20% growth in the enterprise AI platform
market through 2026...
```

The LLM is instructed to "cite by number when using" these documents. This produces responses like:

> Revenue grew 12% to $4.2B [1], with management focusing on AI infrastructure and developer tooling as key growth areas [2]. This aligns with industry projections of 15-20% growth in enterprise AI platforms [3].

### DocumentRecord Schema

```csharp
public sealed class DocumentRecord
{
    [VectorStoreKey]
    public string Id { get; set; } = string.Empty;

    [VectorStoreData]
    public string Text { get; set; } = string.Empty;

    [VectorStoreData]
    public string? Source { get; set; }        // e.g. "report.pdf"

    [VectorStoreData]
    public string? Tags { get; set; }          // e.g. "finance,quarterly"

    [VectorStoreData]
    public string? MetadataJson { get; set; }  // arbitrary JSON

    [VectorStoreVector(1536)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}
```

### Service Registration

RAG is opt-in. Set `AIFoundry:EmbeddingDeploymentName` in appsettings.json to enable:

```csharp
// Program.cs -- conditional registration
if (!string.IsNullOrEmpty(builder.Configuration["AIFoundry:EmbeddingDeploymentName"]))
{
    builder.Services.AddSqliteVectorStore(builder.Configuration);
    builder.Services.AddAiAgentCanvasRag();
}

// AddAiAgentCanvasRag() registers:
// - IEmbeddingGenerator (from Azure AI Foundry)
// - DocumentChunker (recursive text splitter)
// - LlmReranker (LLM-based reranking)
// - RagContextProvider (hybrid search + citations)
```

### Ingestion Example

To ingest a document using the chunker and vector store:

```csharp
var chunker = sp.GetRequiredService<DocumentChunker>();
var collection = sp.GetRequiredService<VectorStoreCollection<string, DocumentRecord>>();
var embedder = sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

var chunks = chunker.Chunk(documentText, source: "annual-report-2024.pdf");

foreach (var chunk in chunks)
{
    var embedding = await embedder.GenerateVectorAsync(chunk.Text);
    await collection.UpsertAsync(new DocumentRecord
    {
        Id = $"report-2024-{chunk.Index}",
        Text = chunk.Text,
        Source = chunk.Source,
        Tags = "finance,annual-report",
        Embedding = embedding.Vector,
    });
}
```

### Architecture Summary

| Component | Location | Purpose |
|-----------|----------|---------|
| `DocumentRecord` | Abstractions | DTO with Id, Text, Source, Tags, Embedding |
| `DocumentChunk` | Abstractions | Output of chunking: Text, Index, Source |
| `RagSearchOptions` | Abstractions | Filters and weights for hybrid search |
| `IHybridSearchable` | Abstractions | Interface for hybrid vector + keyword search |
| `DocumentChunker` | Core | Recursive paragraph/sentence text splitter |
| `LlmReranker` | Core | LLM-based candidate reranking |
| `RagContextProvider` | Core | Orchestrates search, rerank, citation, injection |
| `SqliteDocumentCollection` | VectorStore.Sqlite | SQLite + FTS5 hybrid search implementation |

---

## Security

The `AiAgentCanvas.Security` project integrates Microsoft.AgentGovernance 4.0.0 for policy-based tool governance, prompt injection detection, and MCP gateway control. It also configures ASP.NET rate limiting and security headers.

### Security Overview

Security is registered first in `Program.cs`, before any other service, so that governance is in place before tools are registered:

```csharp
builder.Services.AddAiAgentCanvasSecurity(builder.Configuration);
// ... all other registrations ...
app.UseAiAgentCanvasSecurity();
```

#### What Gets Registered

| Component | Purpose |
|-----------|---------|
| `GovernanceKernel` | Central governance object with policy engine and audit |
| `PolicyEngine` | Evaluates YAML rules against tool calls |
| `AuditEmitter` | Logs all governance events |
| `GovernanceContextProvider` | Scans for prompt injection before each LLM call |
| `GovernedMcpGateway` | Evaluates tool calls against policy for allow/deny decisions |
| `GovernedAIFunction` | DelegatingAIFunction wrapper that runs governance checks before each tool call |
| `GovernanceToolWrapper` | Implements `IToolGovernanceWrapper` (from Abstractions) to wrap all tools at startup |
| Rate limiter | Fixed-window rate limiting on the `/api/copilotkit` endpoint |
| Security headers | X-Content-Type-Options, X-Frame-Options, Referrer-Policy |

### AddAiAgentCanvasSecurity()

The registration method accepts optional configuration callbacks for governance options and MCP gateway config:

```csharp
public static IServiceCollection AddAiAgentCanvasSecurity(
    this IServiceCollection services,
    IConfiguration configuration,
    Action<GovernanceOptions>? configureGovernance = null,
    Action<McpGatewayConfig>? configureMcp = null)
{
    var governanceOptions = new GovernanceOptions
    {
        EnableAudit = true,
        EnableMetrics = true,
        EnablePromptInjectionDetection = true,
        ConflictStrategy = ConflictResolutionStrategy.DenyOverrides,
        PolicyPaths = policyPaths,
    };

    configureGovernance?.Invoke(governanceOptions);

    var kernel = new GovernanceKernel(governanceOptions);
    services.AddSingleton(kernel);
    services.AddSingleton(kernel.PolicyEngine);
    services.AddSingleton(kernel.AuditEmitter);
    // ...
}
```

#### GovernanceOptions Defaults

| Option | Default | Description |
|--------|---------|-------------|
| `EnableAudit` | `true` | Log all governance decisions |
| `EnableMetrics` | `true` | Collect governance metrics |
| `EnablePromptInjectionDetection` | `true` | Scan system instructions for injection |
| `ConflictStrategy` | `DenyOverrides` | When rules conflict, deny wins |
| `PolicyPaths` | From config | Path to `governance-policy.yaml` |

### GovernanceKernel

The `GovernanceKernel` is the central governance object. It is constructed from `GovernanceOptions` and provides access to the `PolicyEngine`, `AuditEmitter`, and `InjectionDetector`.

During startup, `UseAiAgentCanvasSecurity()` subscribes to all governance events for logging:

```csharp
kernel.OnAllEvents(e =>
{
    logger.LogInformation("[GOVERNANCE:AUDIT] Type={Type} Agent={Agent} Policy={Policy}",
        e.Type, e.AgentId, e.PolicyName ?? "none");
});
```

### GovernanceContextProvider

An `AIContextProvider` that runs before each LLM call to scan system instructions for prompt injection attempts.

```csharp
public sealed class GovernanceContextProvider : AIContextProvider
{
    private readonly GovernanceKernel _kernel;

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        if (_kernel.InjectionDetector is null ||
            string.IsNullOrEmpty(context.AIContext.Instructions))
            return new ValueTask<AIContext>(context.AIContext);

        var result = _kernel.InjectionDetector.Detect(context.AIContext.Instructions);
        if (result.IsInjection)
        {
            _logger.LogWarning("[GOVERNANCE] Prompt injection detected: {Type}",
                result.InjectionType);

            _kernel.AuditEmitter.Emit(
                GovernanceEventType.PolicyViolation,
                "system", "instructions",
                new Dictionary<string, object>
                {
                    ["injectionType"] = result.InjectionType.ToString(),
                    ["source"] = "system_instructions",
                });
        }

        return new ValueTask<AIContext>(context.AIContext);
    }
}
```

This catches injection attempts that may have been introduced through user-managed data (personas, context entries, etc.) before they reach the LLM.

### GovernedMcpGateway

Wraps the AgentGovernance `McpGateway` to evaluate tool calls against governance policies. It checks whether a tool call should be allowed, denied, or requires approval.

```csharp
public sealed class GovernedMcpGateway
{
    private readonly McpGateway _gateway;

    public GovernedMcpGateway(McpGatewayConfig config, ILogger<GovernedMcpGateway> logger)
    {
        _gateway = new McpGateway(config);
    }

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
            _logger.LogWarning("[GOVERNANCE:MCP] Blocked tool={Tool} agent={Agent}",
                toolName, agentId);

        return decision;
    }
}
```

#### McpGatewayConfig

```csharp
var mcpConfig = new McpGatewayConfig
{
    BlockOnSuspiciousPayload = true,
    ApprovalRequiredTools = ["run_script", "write_file"],
};
```

| Option | Default | Description |
|--------|---------|-------------|
| `BlockOnSuspiciousPayload` | `true` | Block tools with suspicious payloads |
| `ApprovalRequiredTools` | `["run_script", "write_file"]` | Tools that require explicit approval |

### Tool-Call Governance

Every tool registered at startup is wrapped with governance checks via the `IToolGovernanceWrapper` abstraction. This ensures that **every** tool call goes through policy evaluation, not just MCP calls.

#### How It Works

1. The Security project registers `GovernanceToolWrapper` as `IToolGovernanceWrapper`
2. Core's `AddAiAgentCanvas()` optionally resolves `IToolGovernanceWrapper` from DI
3. If present, each `AIFunction` is wrapped in a `GovernedAIFunction` (a `DelegatingAIFunction`)
4. Before every tool call, `GovernedAIFunction.InvokeCoreAsync()` runs `GovernedMcpGateway.Evaluate()`
5. Audit events are emitted for both allowed and blocked calls
6. Blocked calls return an error JSON to the LLM instead of executing

This is opt-in: if Security is not registered, tools pass through unwrapped. The `IToolGovernanceWrapper` interface lives in Abstractions so Core never references Security directly.

#### GovernedAIFunction

```csharp
public sealed class GovernedAIFunction : DelegatingAIFunction
{
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var decision = _gateway.Evaluate("agent", toolName, payload);

        _auditEmitter.Emit(
            decision.Allowed ? GovernanceEventType.PolicyCheck
                             : GovernanceEventType.ToolCallBlocked,
            "agent", toolName, ...);

        if (!decision.Allowed)
            return /* error JSON */;

        return await base.InvokeCoreAsync(arguments, cancellationToken);
    }
}
```

### Governance Policy (YAML)

Policies are defined in `governance-policy.yaml` and evaluated by the `PolicyEngine`. Each rule has a name, scope, action, conditions, and reason.

```yaml
name: AiAgentCanvas-default
description: Default governance policy for AiAgentCanvas

rules:
  - name: block-dangerous-tools
    scope: tool_call
    action: deny
    conditions:
      tool_name:
        in: [run_script]
    reason: "Shell execution requires explicit approval"

  - name: restrict-file-write-paths
    scope: tool_call
    action: deny
    conditions:
      tool_name:
        equals: write_file
      path:
        matches: "^(/etc|/var|C:\\\\Windows|C:\\\\Program Files)"
    reason: "Writing to system directories is blocked"

  - name: block-private-mcp-endpoints
    scope: tool_call
    action: deny
    conditions:
      tool_name:
        equals: connect_mcp_server
      endpoint:
        matches: "(localhost|127\\.0\\.0\\.1|169\\.254\\.|10\\.|172\\.(1[6-9]|2[0-9]|3[01])\\.|192\\.168\\.)"
    reason: "MCP connections to private addresses are blocked (SSRF protection)"

  - name: allow-all-other
    scope: tool_call
    action: allow
    conditions: {}
```

#### Rule Structure

| Field | Description |
|-------|-------------|
| `name` | Unique rule identifier |
| `scope` | What the rule applies to (`tool_call`) |
| `action` | `allow` or `deny` |
| `conditions` | Matching criteria: `equals`, `in`, `matches` (regex) |
| `reason` | Human-readable explanation logged on match |

Rules are evaluated in order. The `ConflictStrategy` of `DenyOverrides` means if any rule denies, the action is blocked regardless of subsequent allow rules.

#### Configuration

The policy file path is configured in `appsettings.json`:

```json
{
  "Security": {
    "PolicyPath": "governance-policy.yaml",
    "RateLimitPerMinute": 30
  }
}
```

### ASP.NET Rate Limiting

A fixed-window rate limiter protects the agent endpoint:

```csharp
var rateLimitWindow = configuration.GetValue("Security:RateLimitPerMinute", 30);
services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("agent", limiter =>
    {
        limiter.PermitLimit = rateLimitWindow;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});
```

When the limit is exceeded, the response is:

```json
{"error": "Rate limit exceeded. Try again later."}
```

### Security Headers

`UseAiAgentCanvasSecurity()` adds the following headers to every response:

| Header | Value | Purpose |
|--------|-------|---------|
| `X-Content-Type-Options` | `nosniff` | Prevents MIME-type sniffing |
| `X-Frame-Options` | `DENY` | Prevents clickjacking |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Controls referrer information |

### Customizing Security

Override defaults by passing configuration callbacks:

```csharp
builder.Services.AddAiAgentCanvasSecurity(builder.Configuration,
    configureGovernance: options =>
    {
        options.EnablePromptInjectionDetection = true;
        options.ConflictStrategy = ConflictResolutionStrategy.AllowOverrides;
    },
    configureMcp: config =>
    {
        config.ApprovalRequiredTools = ["run_script", "write_file", "delete_file"];
        config.BlockOnSuspiciousPayload = true;
    });
```

---

## Adding Custom Agents

A custom agent in AI Agent Canvas is a self-contained project under `src/Agents/` that seeds all the components the agent needs to function: **persona**, **context**, **workflows**, **entities**, **user profiles**, **guardrails**, **goals**, **skills**, and **MCP connections**. Agents and data connections are separate projects: agents define *how* the LLM behaves, data connections define *what* it can do.

### How It Works

Data connections (like `MCP.HelloWorldData`) register tools as `IReadOnlyList<AITool>` services. Custom agents seed their components via seed interfaces (`IPersonaSeed`, `IContextSeed`, `IWorkflowSeed`, `IEntitySeed`, `IGuardrailSeed`, `IGoalSeed`, `ISkillSeed`, `IMcpConnectionSeed`) that the platform resolves at startup. Seeded data is saved to disk (or database) if it doesn't already exist, preserving any manual edits.

### Step-by-Step Guide

#### Step 1: Create a Project Folder

Create a new folder under `src/Agents/`:

```
src/Agents/Agent.MyAgent/
```

#### Step 2: Create the .csproj

A custom agent only needs two dependencies: `Abstractions` (for the seed interfaces) and `Microsoft.Extensions.DependencyInjection.Abstractions` (for the service extension). No `Microsoft.Extensions.AI` -- agents don't own tools.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.9" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\AiAgentCanvas\AiAgentCanvas.Abstractions\AiAgentCanvas.Abstractions.csproj" />
  </ItemGroup>

</Project>
```

#### Step 3: Create a Service Extension

Register seeds for the components your agent needs. At minimum, seed a persona. Optionally seed context, workflows, entities, guardrails, goals, skills, and MCP connections:

```csharp
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MyAgent;

public static class MyAgentServiceExtensions
{
    public static IServiceCollection AddMyAgent(this IServiceCollection services)
    {
        // Persona: how the LLM behaves
        services.AddSingleton<IPersonaSeed>(new PersonaSeed(
            name: "customer-support",
            description: "Customer support assistant for Contoso Electronics",
            instructions: """
                You are a customer support assistant for Contoso Electronics.
                Use search_kb and lookup_order to help customers.
                Always greet warmly and never share internal pricing.
                """));

        // Guardrail: policy constraint
        services.AddSingleton<IGuardrailSeed>(new GuardrailSeed(
            name: "no-internal-pricing",
            severity: "high",
            enabled: true,
            rule: "Never reveal wholesale costs, margins, or internal pricing to customers."));

        // Workflow: reusable multi-step task
        services.AddSingleton<IWorkflowSeed>(new WorkflowSeed(
            name: "order-investigation",
            description: "Investigate a customer order issue end-to-end",
            tags: "support,orders",
            content: """
                ## Order Investigation
                1. Look up the order with `lookup_order`
                2. Check product details with `search_kb`
                3. Summarize findings and recommend next steps
                """));

        // Context: background knowledge
        services.AddSingleton<IContextSeed>(new ContextSeed(
            topic: "contoso-return-policy",
            tags: "support,policy",
            type: "fact",
            content: "Contoso offers 30-day returns on all electronics..."));

        // Entity: domain schema
        services.AddSingleton<IEntitySeed>(new EntitySeed(
            name: "support-ticket",
            type: "ticket",
            tags: "support",
            content: "Schema: customer name, order number, issue description, resolution..."));

        // Skill: reusable prompt template
        services.AddSingleton<ISkillSeed>(new SkillSeed(
            name: "escalation-summary",
            description: "Generate an escalation summary for a support case",
            promptTemplate: "Summarize the support case for {{customer}} regarding order {{order}}..."));

        // Tool dependencies: validated at startup, warns if missing
        services.AddSingleton<IToolDependencySeed>(new ToolDependencySeed(
            agentName: "customer-support",
            requiredTools: ["search_kb", "lookup_order"]));

        return services;
    }
}
```

The tools referenced in the persona (`search_kb`, `lookup_order`) come from a separate data connection project. The `IToolDependencySeed` declares this dependency explicitly -- at startup the platform validates that all required tools are registered and logs warnings for any missing ones.

#### Step 4: Add ProjectReference in AiAgentCanvas.Orchestrator.csproj

Open `src/Orchestrator/AiAgentCanvas.Orchestrator/AiAgentCanvas.Orchestrator.csproj` and add a reference to your project:

```xml
<ProjectReference Include="..\..\Agents\Agent.MyAgent\Agent.MyAgent.csproj" />
```

#### Step 5: Add to AiAgentCanvas.sln

Add the project to the solution under the Agents solution folder:

```
dotnet sln AiAgentCanvas.sln add src/Agents/Agent.MyAgent/Agent.MyAgent.csproj --solution-folder Agents
```

#### Step 6: Wire Up in Program.cs

```csharp
using MyAgent;

builder.Services.AddMyAgent();
```

The persona is saved to `agent-data/orchestrator/agent/personas/customer-support.md` on first startup. Users activate it with: *"switch to the customer-support persona"*.

### Full Reference: HelloWorldAgent

The `Agent.HelloWorld` in `src/Agents/Agent.HelloWorld/` is the built-in starter example. It demonstrates the complete custom agent pattern -- a financial analyst that seeds all component types and references tools from the `MCP.HelloWorldData` data connection.

#### HelloWorldServiceExtensions.cs

The entire agent is a single file that seeds every component type:

| Seed | Name | Purpose |
|------|------|---------|
| `IPersonaSeed` | `financial-analyst` | Role instructions referencing market data tools |
| `IContextSeed` | `financial-analysis-methodology` | Reference material for P/E, revenue growth, EPS, etc. |
| `IWorkflowSeed` | `full-stock-analysis` | Quote -> history -> fundamentals -> summary |
| `IEntitySeed` | `stock-analysis-report` | Schema for structured analysis output |
| `IGuardrailSeed` | `investment-disclaimer` | Never provide buy/sell recommendations |
| `ISkillSeed` | `compare-stocks` | Prompt template for side-by-side stock comparison |
| `IToolDependencySeed` | `financial-analyst` | Declares dependency on stock_quote, stock_history, edgar_company_facts |

The tools (`stock_quote`, `stock_history`, `edgar_company_facts`) are registered by the `MCP.HelloWorldData` data connection in a separate project. The agent doesn't own or define any tools -- it only tells the LLM how to use them.

Wired in `Program.cs` with a single line: `builder.Services.AddHelloWorldAgent();`

### Component Seeding

Seed interfaces let custom agents ship their own data. At startup, the platform resolves all registered seed services and saves any that don't already exist on disk (or in the database for skills). This means:

- Components are created automatically on first run
- Manual edits to persisted files are preserved (seeds never overwrite)
- Users interact with seeded components the same way as manually created ones
- Seed data is written to the per-agent `agent-data/<agent>/agent/` directory

**No-code alternative:** Instead of implementing seed interfaces in code, you can drop hand-written markdown files into the `agent-data/<agent>/user/` directories. These files are read alongside system-created data but are never overwritten by the system. Use the same YAML frontmatter format shown in the [Agent Data](#agent-data) section.

| Interface | Concrete Class | Fields | Persisted To |
|-----------|---------------|--------|-------------|
| `IPersonaSeed` | `PersonaSeed` | name, description, instructions | `agent-data/<agent>/agent/personas/*.md` |
| `IContextSeed` | `ContextSeed` | topic, type, tags, content | `agent-data/<agent>/agent/context/*.md` |
| `IWorkflowSeed` | `WorkflowSeed` | name, description, tags, content | `agent-data/<agent>/agent/workflows/*.md` |
| `IEntitySeed` | `EntitySeed` | name, type, tags, content | `agent-data/<agent>/agent/entities/*.md` |
| `IGuardrailSeed` | `GuardrailSeed` | name, severity, enabled, rule | `agent-data/<agent>/agent/guardrails/*.md` |
| `IGoalSeed` | `GoalSeed` | name, description, priority, acceptanceCriteria, assignedAgent, content | `agent-data/<agent>/agent/goals/*.md` |
| `ISkillSeed` | `SkillSeed` | name, description, promptTemplate | `skills.db` (SQLite) |
| `IMcpConnectionSeed` | `McpConnectionSeed` | name, endpoint, transport | In-memory (connected at startup) |
| `IToolDependencySeed` | `ToolDependencySeed` | agentName, requiredTools | Validated at startup (warnings for missing tools) |

### Switching Between Agents

Users switch agents at runtime via personas -- no restart needed. Say *"switch to the hello-world persona"* or *"switch to the customer-support persona"*. Say *"switch to default"* to return to the base system prompt. See [Agent Data](#agent-data) for details.

### Testing Your Agent

1. Build the solution: `dotnet build AiAgentCanvas.sln`
2. Run the backend: `cd src/Orchestrator/AiAgentCanvas.Orchestrator && dotnet run`
3. Run the frontend: `cd frontend && npm run dev`
4. Open the CopilotKit UI and interact with your agent
5. Check the console logs for tool calls and governance events

---

## Adding Custom MCP Connections

Custom MCP connections in AI Agent Canvas are projects that provide tools (data connections, API integrations) to the agent. Each connection is a class library that defines tools using `AIFunctionFactory.Create()` and registers them as `IReadOnlyList<AITool>` services.

The term "MCP connection" here refers to a local tool provider project (not an external MCP server -- for connecting to remote MCP servers at runtime, see [Skills & MCP](#skills--mcp)).

### How Tool Registration Works

The Core platform collects all `IReadOnlyList<AITool>` singleton services at startup and passes them to the `ChatClientAgent`:

```csharp
// Inside AddAiAgentCanvas() in Core:
var tools = sp.GetServices<IReadOnlyList<AITool>>().SelectMany(t => t).ToList();
var agentOptions = new ChatClientAgentOptions
{
    ChatOptions = new ChatOptions { Tools = tools },
};
```

Your custom project just needs to register its tools as `IReadOnlyList<AITool>` and they automatically become available to the LLM.

### Step-by-Step Guide (MCP)

#### Step 1: Create the Project

Create a new folder under `src/Custom/` and a `.csproj` with the `Microsoft.Extensions.AI` package:

```
src/Custom/MCP.Weather/
```

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.AI" Version="10.7.0" />
  </ItemGroup>

</Project>
```

Add `Microsoft.Extensions.Http.Resilience` if you need HTTP clients with retry policies.

#### Step 2: Create a ToolProvider Class

Define your tools using `AIFunctionFactory.Create()`. Each tool is a method decorated with `[Description]` attributes:

```csharp
using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MCP.Weather;

public sealed class WeatherToolProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;
    private readonly ILogger<WeatherToolProvider> _logger;

    public WeatherToolProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<WeatherToolProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = configuration["Weather:ApiKey"] ?? "";
        _logger = logger;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(GetCurrentWeather, "get_current_weather",
                "Get current weather conditions for a city"),
            AIFunctionFactory.Create(GetForecast, "get_weather_forecast",
                "Get a 5-day weather forecast for a city"),
        ];
    }

    [Description("Get current weather conditions for a city")]
    private async Task<string> GetCurrentWeather(
        [Description("City name (e.g. 'Seattle', 'London')")] string city,
        CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Weather");
            var url = $"https://api.weather.example.com/current?q={city}&key={_apiKey}";
            var result = await client.GetStringAsync(url, ct);

            _logger.LogInformation("Weather fetched for {City}", city);
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Weather fetch failed for {City}", city);
            return JsonSerializer.Serialize(new { error = $"Failed: {ex.Message}" });
        }
    }

    [Description("Get a 5-day weather forecast for a city")]
    private async Task<string> GetForecast(
        [Description("City name")] string city,
        [Description("Number of days (1-5)")] int days,
        CancellationToken ct)
    {
        // Implementation similar to above
        return "...";
    }
}
```

Key points:

- The constructor receives dependencies from DI (IHttpClientFactory, IConfiguration, ILogger)
- `GetTools()` returns the tool list using `AIFunctionFactory.Create()`
- Each tool method uses `[Description]` attributes so the LLM understands parameters
- Tools return JSON strings (the LLM parses the response)
- Always handle errors gracefully and return error JSON instead of throwing

#### Step 3: Create ServiceExtensions

Create an extension method that registers the HttpClient, the tool provider, and the tools:

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace MCP.Weather;

public static class WeatherServiceExtensions
{
    public static IServiceCollection AddWeatherTools(this IServiceCollection services)
    {
        services.AddHttpClient("Weather", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .AddStandardResilienceHandler();

        services.AddSingleton<WeatherToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<WeatherToolProvider>().GetTools());

        return services;
    }
}
```

The critical line is the `IReadOnlyList<AITool>` registration. This is how the Core platform discovers your tools.

#### Step 4: Wire Up in Program.cs

Add the project reference and call your extension method:

```xml
<!-- In AiAgentCanvas.Web.csproj -->
<ItemGroup>
  <ProjectReference Include="..\Custom\MCP.Weather\MCP.Weather.csproj" />
</ItemGroup>
```

```csharp
// In Program.cs
using MCP.Weather;

builder.Services.AddWeatherTools();
```

Add the project to the solution:

```
dotnet sln AiAgentCanvas.sln add src/Custom/MCP.Weather/MCP.Weather.csproj --solution-folder Custom
```

### Full Reference: MCP.HelloWorldData

The `MCP.HelloWorldData` project in `src/Custom/MCP.HelloWorldData/` is the included sample implementation. It provides three tools for stock market data.

#### MarketDataToolProvider.cs

The tool provider defines three tools:

```csharp
public IReadOnlyList<AITool> GetTools()
{
    return
    [
        AIFunctionFactory.Create(EdgarCompanyFactsAsync, "edgar_company_facts",
            "Fetch SEC EDGAR financial data for a company by ticker symbol"),
        AIFunctionFactory.Create(StockQuoteAsync, "stock_quote",
            "Get current stock price from Yahoo Finance"),
        AIFunctionFactory.Create(StockHistoryAsync, "stock_history",
            "Get historical stock data from Yahoo Finance with configurable range"),
    ];
}
```

Each tool:

- Takes typed parameters with `[Description]` attributes
- Uses `IHttpClientFactory` to make HTTP requests
- Returns JSON strings with structured data or error information
- Logs timing and errors via `ILogger`
- Accepts `CancellationToken` for proper cancellation

#### MarketDataServiceExtensions.cs

```csharp
public static IServiceCollection AddMarketDataTools(this IServiceCollection services)
{
    services.AddHttpClient("SEC", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "AiAgentCanvas/1.0 (contact@example.com)");
        })
        .AddStandardResilienceHandler();

    services.AddHttpClient("YahooFinance", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        })
        .AddStandardResilienceHandler();

    services.AddSingleton<MarketDataToolProvider>();
    services.AddSingleton<IReadOnlyList<AITool>>(sp =>
        sp.GetRequiredService<MarketDataToolProvider>().GetTools());

    return services;
}
```

Notable patterns:

- Named HttpClients for different API backends
- `AddStandardResilienceHandler()` for automatic retry and circuit-breaking
- Custom User-Agent header for the SEC EDGAR API (which requires identification)
- Separate timeout values per API

### Testing Tools

Once registered, your tools are automatically discovered by the agent. To test:

1. Build: `dotnet build AiAgentCanvas.sln`
2. Run: `cd src/AiAgentCanvas.Web && dotnet run`
3. Open the CopilotKit frontend
4. Ask the agent to use your tool (e.g., "What's the weather in Seattle?")
5. Check the backend console for tool call logs

The LLM discovers tools from their names and descriptions. Make sure your tool names are descriptive and your `[Description]` attributes clearly explain what each parameter expects.

### Tool Design Guidelines

1. **Return JSON strings** -- The LLM parses your tool's return value. Always return structured JSON.
2. **Handle errors gracefully** -- Return `{ "error": "..." }` instead of throwing exceptions.
3. **Use CancellationToken** -- Accept and pass through the cancellation token for proper request cancellation.
4. **Log appropriately** -- Use `ILogger` for timing, errors, and debugging.
5. **Keep tools focused** -- One tool per API operation. Let the LLM compose multiple tool calls.
6. **Descriptive names** -- Use snake_case names that clearly describe the action (e.g., `get_current_weather`, not `weather`).
7. **Document parameters** -- Every parameter needs a `[Description]` attribute with examples.

### Tools Without External APIs

Not every tool needs HTTP calls. You can create tools that do local computation, file operations, or database queries:

```csharp
public IReadOnlyList<AITool> GetTools()
{
    return
    [
        AIFunctionFactory.Create(CalculateCompoundInterest, "calculate_compound_interest",
            "Calculate compound interest over a period"),
    ];
}

[Description("Calculate compound interest")]
private string CalculateCompoundInterest(
    [Description("Principal amount")] decimal principal,
    [Description("Annual interest rate (e.g. 0.05 for 5%)")] decimal rate,
    [Description("Number of years")] int years)
{
    var result = principal * (decimal)Math.Pow((double)(1 + rate), years);
    return JsonSerializer.Serialize(new { principal, rate, years, futureValue = result });
}
```

These tools follow the same registration pattern and are automatically available to the agent.

---

> **[Download the complete PDF guide](guides/AI-Agent-Canvas-Guide.pdf)** | **[AI-First Company Guide](guides/AI-First-Company-Guide.pdf)**
