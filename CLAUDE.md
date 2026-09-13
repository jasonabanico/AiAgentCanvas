# AI Agent Canvas

Multi-agent enterprise copilot: .NET 9 backend + Next.js/CopilotKit frontend, built on Microsoft Agent Framework (MAF) and the AG-UI protocol (SSE).

## Architecture

All projects sit flat under `src/`, grouped by solution folders in Visual Studio:

- **Platform** — engine and cross-cutting concerns:
  - `AiAgentCanvas.Abstractions` — shared interfaces (`IServiceModule`, seed contracts, `IAgentMessaging`, `IAgentRegistry`, `IAgentHandoff`, `INotificationSink`, etc.)
  - `AiAgentCanvas.Orchestration` — MAF agent wiring, AG-UI endpoint, agent registry/handoff, inter-agent messaging
  - `AiAgentCanvas.Security` — Microsoft Agent Governance Toolkit + Purview integration
  - `AiAgentCanvas.Storage.Sqlite` — SQLite-backed chat history
- **Capabilities** — opt-in feature modules, each behind a `Features:*` flag: `Rag`, `Scheduling` (Hangfire), `Skills`, `Notifications`, `SystemTools`, `EpisodicMemory`, `AuditLog`, `EventTriggers`, `ComputerUse` (Playwright browser automation), `Evaluation` (LLM-as-judge eval cases/results, scored against the raw `IChatClient`)
- **AgentData** — MD-persisted agent state: `Personas`, `Context`, `Entities`, `Guardrails`, `Profiles`, `Workflows`
- **Agents** — specialist agent projects, e.g. `Agent.FinancialAnalyst` (sample: financial analysis persona + tools)
- **DataConnections** — tool providers and vector stores: `DataConnection.MarketData` (Yahoo Finance + SEC EDGAR), `DataConnection.VectorStore.Sqlite`, `DataConnection.VectorSearch.Databricks`, `DataConnection.VectorSearch.Snowflake`
- **Providers** — swappable LLM/data backends selected by the `Provider` config key: `AiAgentCanvas.Providers.AzureAIFoundry`, `AiAgentCanvas.Providers.Databricks`, `AiAgentCanvas.Providers.Snowflake`
- **Host** — `AiAgentCanvas.Host`, the composition root (`Program.cs`)

## Build & Run

```bash
# Backend
dotnet build AiAgentCanvas.sln
cd src/Host/AiAgentCanvas.Host && dotnet run

# Frontend
cd frontend && npm install && npm run dev
```

## Key conventions

- Agents and data connections implement `IServiceModule` (in `AiAgentCanvas.Abstractions`): `SectionName` names the config section that gates them, `ConfigureServices` registers their seeds/tools. A module only loads if its section has `Enabled: true`.
- `Program.cs` wires built-in capabilities directly behind flags in `FeatureFlags` (`Features:*` config section). External agent/data-connection plugins are discovered at runtime by `ServiceModuleExtensions.AddServiceModules`: one subfolder per plugin under `plugins/`, loaded into its own `PluginLoadContext`, scanned by reflection for `IServiceModule` types. The Host never references a plugin assembly directly — dropping a plugin folder in or out changes what loads with no project-reference change.
- Agents seed their behavior via typed seed interfaces (`IPersonaSeed`, `IContextSeed`, `IWorkflowSeed`, `IEntitySeed`, `IGuardrailSeed`, `ISkillSeed`, `IUserProfileSeed`, `IAgentToolsSeed`, `IMcpConnectionSeed`) rather than code — see `Agent.FinancialAnalyst/FinancialAnalystServiceExtensions.cs` for the canonical example. Seeds are saved to disk on first run and never overwrite manual edits.
- **Agents and data connections are separate projects.** Agents define *how* the LLM behaves (personas, context, workflows, guardrails); data connections define *what* it can do (tools). An agent's persona references tools by name only — multiple agents can share the same `DataConnection.*` project.
- LLM/data backend is selected by the `Provider` config value (`AzureAIFoundry` | `Databricks` | `Snowflake`); each `Providers.*` project registers its own chat client and (where supported) embeddings via its own `Add*` extension.
- AG-UI protocol (SSE) endpoint and agent orchestration live in `AiAgentCanvas.Orchestration`; Host calls `builder.Services.AddAiAgentCanvas(config, options)` + `app.UseAiAgentCanvas()`.
- Provider config lives under `AIFoundry` / `Databricks` / `Snowflake` sections in `appsettings.json`; capability flags live under `Features`.
