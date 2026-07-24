# 9. Architecture

The platform is organized into five layers. Each layer depends only on the layers below it. This constraint keeps the dependency graph clean -- you can change a capability without touching the platform, or swap a data connection without affecting agent logic.

## Layer Overview

```
┌─────────────────────────────────────────────────────┐
│  Host                                               │
│  ASP.NET Core composition root                      │
├─────────────────────────────────────────────────────┤
│  Agents                                             │
│  Custom agent projects (persona, tools, seeds)      │
├─────────────────────────────────────────────────────┤
│  Platform                                           │
│  Orchestration, abstractions, security              │
├──────────────────────┬──────────────────────────────┤
│  Capabilities        │  AgentData                   │
│  Cross-cutting       │  Six context domains         │
│  features            │                              │
├──────────────────────┴──────────────────────────────┤
│  DataConnections                                    │
│  Storage and external data                          │
└─────────────────────────────────────────────────────┘
```

| Layer | What It Does |
|---|---|
| **Host** | ASP.NET Core composition root. Wires all layers together, runs the web server, maps HTTP endpoints. The only project that references everything else. |
| **Agents** | Custom agent projects. Each registers its persona, tools, and seeds via an extension method. Agent projects reference only Platform.Abstractions. |
| **Platform** | Orchestration engine, shared abstractions, and security. Contains the agent runtime, registry, handoff, AG-UI server, A2A server, and governance. |
| **Capabilities** | Cross-cutting features that any agent can use. Skills, scheduling, notifications, system tools, and RAG. |
| **AgentData** | Six context domains that store and provide agent knowledge: personas, context, entities, guardrails, profiles, and workflows. |
| **DataConnections** | Storage backends and external data sources. SQLite for persistence, vector store for RAG, market data for domain-specific tools. |

## Project Reference Map

```
Host
├── Platform.Orchestration
│   └── Platform.Abstractions
├── Platform.Security
│   └── Platform.Abstractions
├── Agent.FinancialAnalyst
│   └── Platform.Abstractions
├── Capabilities.Skills
│   ├── Platform.Abstractions
│   └── Platform.Orchestration
├── Capabilities.Scheduling
│   ├── Platform.Abstractions
│   └── AgentData.Personas
├── Capabilities.Notifications
│   └── Platform.Abstractions
├── Capabilities.SystemTools
│   └── Platform.Abstractions
├── Capabilities.Rag
│   └── Platform.Abstractions
├── AgentData.Personas
│   └── Platform.Abstractions
├── AgentData.Context
│   └── Platform.Abstractions
├── AgentData.Entities
│   └── Platform.Abstractions
├── AgentData.Guardrails
│   └── Platform.Abstractions
├── AgentData.Profiles
│   └── Platform.Abstractions
├── AgentData.Workflows
│   ├── Platform.Abstractions
│   └── Platform.Orchestration
├── DataConnections.Storage.Sqlite
│   └── Platform.Abstractions
├── DataConnections.VectorStore.Sqlite
│   └── Platform.Abstractions
├── DataConnections.MarketData
│   └── Platform.Abstractions
└── Providers.AzureAIFoundry
```

## Projects in Detail

### Platform Layer

The platform provides the runtime that powers every agent. It is split into three projects to separate concerns.

| Project | Purpose | Key Types |
|---|---|---|
| **Platform.Abstractions** | Interfaces and contracts. Every other project references this. No implementations. | `IAgentRegistry`, `IAgentHandoff`, `IAgentMessaging`, `IPersonaSeed`, `IAgentToolsSeed`, `IContextSeed`, `IWorkflowSeed`, `IEntitySeed`, `IGuardrailSeed`, `ISkillSeed`, `IUserProfileSeed`, `IGoalSeed`, `IMcpConnectionSeed`, `ToolStateMapping` |
| **Platform.Orchestration** | Agent runtime and coordination. Builds agents from seeds, manages handoff, runs the AG-UI and A2A servers. | `AgentRegistry`, `InProcessAgentHandoff`, `InProcessAgentMessaging`, `HandoffToolProvider`, `ToolDeduplicatingChatClient`, `CoreServiceExtensions` |
| **Platform.Security** | Governance and policy enforcement. Wraps tool calls with policy checks, injects security context. | `GovernedAIFunction`, `GovernanceToolWrapper`, `GovernedMcpGateway`, `GovernanceContextProvider` |

### AgentData Layer

Six domain-specific projects that store and provide agent knowledge. Each follows the same pattern: a store for persistence, tools for LLM access, and a context provider for automatic injection.

| Domain | Store | Tools | Context Provider |
|---|---|---|---|
| **Personas** | `PersonaStore` | `PersonaToolProvider` (list, read, set active) | `PersonaContextProvider` -- injects active persona instructions |
| **Context** | `PersistentContextStore` | `ContextToolProvider` (add, list, remove) | `PersistentContextProvider` -- injects all stored context entries |
| **Entities** | `EntityStore` | `EntityToolProvider` (create, read, update, list) | `EntityContextProvider` -- injects entity index |
| **Guardrails** | `GuardrailStore` | `GuardrailToolProvider` (list, read, set active) | `GuardrailContextProvider` -- injects active guardrail rules |
| **Profiles** | `UserProfileStore` | `UserProfileToolProvider` (read, update) | `UserProfileContextProvider` -- injects user profile context |
| **Workflows** | `WorkflowStore` | `WorkflowToolProvider` (create, list, run, run_sequential, run_concurrent) | None (workflows are invoked explicitly, not injected) |

### Capabilities Layer

Cross-cutting features that agents use but that are not specific to any single domain.

| Capability | What It Provides | Key Types |
|---|---|---|
| **Skills** | Reusable, parameterized procedures agents can invoke by name | `SkillStore`, `SkillToolProvider`, `SkillExecutor` |
| **Scheduling** | Cron-based and one-time scheduled agent tasks | `ScheduleStore`, `ScheduleToolProvider`, `ScheduleExecutor` |
| **Notifications** | Agent-to-user notification delivery | `NotificationStore`, `NotificationToolProvider` |
| **SystemTools** | General-purpose tools: date/time, math, web search, file operations | `SystemToolProvider` |
| **RAG** | Vector-based retrieval-augmented generation with citation tracking | `RagToolProvider`, `RagContextProvider`, `VectorSearchService` |

### DataConnections and Providers

Storage backends and external data sources. These projects implement the persistence and data-access interfaces defined in Abstractions.

**DataConnections:**

| Project | Purpose | Key Types |
|---|---|---|
| **Storage.Sqlite** | SQLite-backed persistence for all stores (personas, entities, context, schedules, etc.) | `SqliteStorageProvider`, migration scripts |
| **VectorStore.Sqlite** | SQLite-backed vector store for RAG embeddings | `SqliteVectorStore` |
| **MarketData** | Stock quotes, price history, and SEC EDGAR company filings | `StockQuoteTool`, `StockHistoryTool`, `EdgarCompanyFactsTool` |

**Providers:**

| Project | Purpose |
|---|---|
| **Providers.AzureAIFoundry** | LLM client configuration for Azure AI Foundry (Azure OpenAI) endpoints |

## Frontend

The frontend is a Next.js 15 application using React 19. It connects to the backend via the AG-UI protocol -- a raw SSE (Server-Sent Events) client that consumes the event stream directly.

There is no CopilotKit SDK dependency. The frontend implements the AG-UI client from scratch, handling event types like `TEXT_MESSAGE_CONTENT`, `TOOL_CALL_START`, `TOOL_CALL_END`, `STATE_SNAPSHOT`, and `STATE_DELTA` to render the chat interface, tool activity, and live state updates.

## Request Flow

A user message travels through the system in this sequence:

1. **Frontend POSTs to `/api/copilotkit`** -- the AG-UI endpoint. The request contains the user message, conversation history, and frontend state.

2. **AG-UI server resolves the session** -- identifies the user, loads conversation history, and prepares the agent context.

3. **Context providers inject data** -- each registered `AIContextProvider` appends its domain knowledge to the system prompt: persona instructions, guardrail rules, entity index, user profile, RAG results, and governance context.

4. **HarnessAgent runs the agent loop** -- the agent built via `AsHarnessAgent` executes the reason-act-observe cycle. The LLM receives the assembled prompt with all context and tool definitions.

5. **ToolDeduplicatingChatClient removes duplicates** -- before the request reaches the LLM, this delegating client strips duplicate tool definitions (which can occur when multiple providers register overlapping tools).

6. **GovernedAIFunction evaluates tool calls** -- every tool invocation passes through the governance wrapper. The `GovernedMcpGateway` evaluates the call against active policies. Blocked calls return an error message instead of executing. All decisions are logged as audit events.

7. **Results stream as AG-UI SSE events** -- text content, tool call progress, state updates, and completion signals stream back to the frontend as Server-Sent Events. The frontend renders them incrementally as they arrive.

```
Frontend                    Host                         LLM
   │                         │                            │
   │── POST /api/copilotkit ─>│                            │
   │                         │── resolve session           │
   │                         │── inject context            │
   │                         │── build prompt + tools      │
   │                         │── send to LLM ─────────────>│
   │                         │                            │── reason
   │                         │<── tool call ──────────────│
   │                         │── governance check          │
   │                         │── execute tool              │
   │                         │── tool result ─────────────>│
   │                         │                            │── reason
   │                         │<── text response ──────────│
   │<── SSE: text content ──│                            │
   │<── SSE: state update ──│                            │
   │<── SSE: run complete ──│                            │
```
