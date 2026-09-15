# AI Agent Canvas

Multi-agent enterprise copilot: .NET 9 backend + Next.js frontend, built on Microsoft Agent Framework (MAF) and the AG-UI protocol (SSE).

## Architecture

All projects sit flat under `src/`, grouped by solution folders in Visual Studio:

- **Platform** — engine and cross-cutting concerns:
  - `AiAgentCanvas.Abstractions` — shared interfaces (`IServiceModule`, seed contracts, `IAgentMessaging`, `IAgentRegistry`, `IAgentHandoff`, `INotificationSink`), plus `CronSchedule`, `AgentTelemetry`, and `AgentClientKeys`
  - `AiAgentCanvas.Orchestration` — MAF agent wiring, AG-UI endpoint, agent registry/handoff, inter-agent messaging, and the chat-client pipeline (context budget, loop guard, model router, reflection, tool dedupe, tool tracing)
  - `AiAgentCanvas.Security` — Microsoft Agent Governance Toolkit + Purview integration
  - `AiAgentCanvas.Storage.Sqlite` — SQLite-backed chat history
- **Capabilities** — opt-in feature modules, each behind a `Features:*` flag: `Rag`, `Scheduling`, `Skills`, `Notifications`, `SystemTools`, `EpisodicMemory`, `AuditLog`, `EventTriggers`, `ComputerUse` (Playwright browser automation), `Evaluation` (LLM-as-judge run against the built agent)
- **AgentData** — MD-persisted agent state: `Personas`, `Context`, `Entities`, `Guardrails`, `Profiles`, `Workflows`
- **Agents** — specialist agent projects, e.g. `Agent.FinancialAnalyst` (sample: financial analysis persona + tools)
- **DataConnections** — tool providers and vector stores: `DataConnection.MarketData` (Yahoo Finance + SEC EDGAR), `DataConnection.VectorStore.Sqlite`, `DataConnection.VectorSearch.Databricks`, `DataConnection.VectorSearch.Snowflake`
- **Providers** — swappable LLM/data backends selected by the `Provider` config key: `AiAgentCanvas.Providers.AzureAIFoundry`, `AiAgentCanvas.Providers.Databricks`, `AiAgentCanvas.Providers.Snowflake`
- **Host** — `AiAgentCanvas.Host`, the composition root (`Program.cs`)
- **tests/AiAgentCanvas.Tests** — xUnit coverage of the deterministic runtime pieces

## Build & Run

```bash
# Backend
dotnet build AiAgentCanvas.sln
cd src/Host/AiAgentCanvas.Host && dotnet run

# Tests
dotnet test tests/AiAgentCanvas.Tests/AiAgentCanvas.Tests.csproj

# Frontend
cd frontend && npm install && npm run dev
```

## Key conventions

- Agents and data connections implement `IServiceModule` (in `AiAgentCanvas.Abstractions`): `SectionName` names the config section that gates them, `ConfigureServices` registers their seeds/tools. A module only loads if its section has `Enabled: true`.
- `Program.cs` wires built-in capabilities directly behind flags in `FeatureFlags` (`Features:*` config section). External agent/data-connection plugins are discovered at runtime by `ServiceModuleExtensions.AddServiceModules`: one subfolder per plugin under `plugins/`, loaded into its own `PluginLoadContext`, scanned by reflection for `IServiceModule` types. The Host never references a plugin assembly directly.
- Agents seed their behavior via typed seed interfaces (`IPersonaSeed`, `IContextSeed`, `IWorkflowSeed`, `IEntitySeed`, `IGuardrailSeed`, `ISkillSeed`, `IUserProfileSeed`, `IAgentToolsSeed`, `IMcpConnectionSeed`) rather than code — see `Agent.FinancialAnalyst/FinancialAnalystServiceExtensions.cs` for the canonical example. Seeds are saved to disk on first run and never overwrite manual edits.
- **Agents and data connections are separate projects.** Agents define *how* the LLM behaves (personas, context, workflows, guardrails); data connections define *what* it can do (tools). An agent's persona references tools by name only — multiple agents can share the same `DataConnection.*` project.
- LLM/data backend is selected by the `Provider` config value (`AzureAIFoundry` | `Databricks` | `Snowflake`); each `Providers.*` project registers its own chat client and, where configured, embeddings plus keyed `economy` and `judge` clients.
- AG-UI protocol (SSE) endpoint and agent orchestration live in `AiAgentCanvas.Orchestration`; Host calls `builder.Services.AddAiAgentCanvas(config, options)` + `app.UseAiAgentCanvas()`.
- Provider config lives under `AIFoundry` / `Databricks` / `Snowflake` sections in `appsettings.json`; capability flags live under `Features`; runtime limits live under `Agent`.

## Runtime invariants

These exist because an agent without them fails in ways that produce no error:

- **Every prompt is counted and budgeted.** `ContextBudgetChatClient` counts with the model's tokenizer, enforces per-component ceilings (`Agent:ContextBudget`), and compacts history with a summarizer rather than letting the provider truncate the prompt silently.
- **Every run has an exit condition.** `LoopGuardChatClient` enforces a tool-round cap, a token budget, repeated-action detection and stagnation detection (`Agent:LoopGuard`). When one trips it withdraws the tools and instructs the model to report what it has.
- **Maker and checker are separate.** `EvaluationRunner` runs a case through the built agent and grades it with the keyed `judge` client. It falls back to the primary model when no judge is configured, and flags every result it produces that way.
- **Filesystem and shell access are allowlisted, and empty means deny.** `SystemToolOptions.AllowedPaths` and `AllowedCommands` both deny everything when empty. Commands run without a shell.
- **Governance approval lists reference real tool names.** `Security:ApprovalRequiredTools` must match the names tools register under (`SystemToolNames`), or it silently protects nothing.
- **Background producers have consumers.** The scheduler has `ScheduledTaskRunner`; event triggers have `TriggerDispatchService`; the trigger queue is bounded. A producer without a consumer is a feature that accepts work and never does it.
