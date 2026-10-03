# AI Agent Canvas

Multi-agent enterprise copilot: .NET 10 backend + Next.js frontend, built on Microsoft Agent Framework (MAF) and the AG-UI protocol (SSE).

## Architecture

All projects sit flat under `src/`, grouped by solution folders in Visual Studio:

- **Platform** — engine and cross-cutting concerns:
  - `AiAgentCanvas.Abstractions` — shared interfaces (`IServiceModule`, seed contracts, `IAgentMessaging`, `IAgentRegistry`, `IAgentHandoff`, `INotificationSink`), plus `CronSchedule`, `AgentTelemetry`, and `AgentClientKeys`
  - `AiAgentCanvas.Connections` — encrypted credential store (Data Protection), OAuth authorization code flow with PKCE, single-flight token refresh, `ICredentialProvider`
  - `AiAgentCanvas.Connectors` — connector contracts (`IConnectorDefinition`, `IToolConnector`, `IEventSourceConnector`), `ConnectorHost`, the guarded per-connection HTTP client, and the webhook route
  - `AiAgentCanvas.Orchestration` — MAF agent wiring, AG-UI endpoint, agent registry/handoff, inter-agent messaging, and the chat-client pipeline (context budget, loop guard, model router, reflection, tool dedupe, tool tracing)
  - `AiAgentCanvas.Security` — Microsoft Agent Governance Toolkit + Purview integration
  - `AiAgentCanvas.Storage.Sqlite` — SQLite-backed chat history
- **Capabilities** — opt-in feature modules, each behind a `Features:*` flag: `Rag`, `Scheduling`, `Skills`, `Notifications`, `SystemTools`, `EpisodicMemory`, `AuditLog`, `EventTriggers`, `ComputerUse` (Playwright browser automation), `RunLedger`, `Jobs`, `Connections`, `Connectors` (needs `Connections`)
- **Connectors** — `AiAgentCanvas.Connector.TwilioSms` and `AiAgentCanvas.Connector.Mcp` (Gmail and any MCP server). A connector is a definition plus one instance per stored connection; see `docs/design/connectors.md`
- **AgentData** — MD-persisted agent state: `Personas`, `Context`, `Entities`, `Guardrails`, `Profiles`, `Workflows`
- **Agents** — specialist agent projects, e.g. `Agent.FinancialAnalyst` (sample: financial analysis persona + tools)
- **DataConnections** — tool providers and vector stores: `DataConnection.MarketData` (Yahoo Finance + SEC EDGAR), `DataConnection.VectorStore.Sqlite`, `DataConnection.VectorSearch.Databricks`, `DataConnection.VectorSearch.Snowflake`
- **Providers** — swappable LLM/data backends selected by the `Provider` config key: `AiAgentCanvas.Providers.AzureAIFoundry`, `AiAgentCanvas.Providers.Databricks`, `AiAgentCanvas.Providers.Snowflake`
- **Host** — `AiAgentCanvas.Host`, the composition root (`Program.cs`)
- **tests/AiAgentCanvas.Tests** — xUnit coverage of the deterministic runtime pieces
- **tests/AiAgentCanvas.EvalTests** — checked-in model evaluations, run in CI

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

## Dependencies

Package versions live only in `Directory.Packages.props` (Central Package Management);
csproj files carry bare `PackageReference` entries with no `Version` attribute. Transitive
pinning is on, so a vulnerable transitive package is fixed by adding a `PackageVersion`
entry rather than waiting for its parent to update. `nuget.config` scopes the repo to
nuget.org with source mapping.

## Key conventions

- Agents and data connections implement `IServiceModule` (in `AiAgentCanvas.Abstractions`): `SectionName` names the config section that gates them, `ConfigureServices` registers their seeds/tools. A module only loads if its section has `Enabled: true`.
- `Program.cs` wires built-in capabilities directly behind flags in `FeatureFlags` (`Features:*` config section). External agent/data-connection plugins are discovered at runtime by `ServiceModuleExtensions.AddServiceModules`: one subfolder per plugin under `plugins/`, loaded into its own `PluginLoadContext`, scanned by reflection for `IServiceModule` types. The Host never references a plugin assembly directly.
- Agents seed their behavior via typed seed interfaces (`IPersonaSeed`, `IContextSeed`, `IWorkflowSeed`, `IEntitySeed`, `IGuardrailSeed`, `ISkillSeed`, `IUserProfileSeed`, `IAgentToolsSeed`, `IMcpConnectionSeed`) rather than code — see `Agent.FinancialAnalyst/FinancialAnalystServiceExtensions.cs` for the canonical example. Seeds are saved to disk on first run and never overwrite manual edits.
- **Agents and data connections are separate projects.** Agents define *how* the LLM behaves (personas, context, workflows, guardrails); data connections define *what* it can do (tools). An agent's persona references tools by name only — multiple agents can share the same `DataConnection.*` project.
- LLM/data backend is selected by the `Provider` config value (`AzureAIFoundry` | `Databricks` | `Snowflake`); each `Providers.*` project registers its own chat client and, where configured, embeddings plus keyed `economy` and `judge` clients.
- AG-UI protocol (SSE) endpoint and agent orchestration live in `AiAgentCanvas.Orchestration`; Host calls `builder.Services.AddAiAgentCanvas(config, options)` + `app.UseAiAgentCanvas()`.
- Provider config lives under `AIFoundry` / `Databricks` / `Snowflake` sections in `appsettings.json`; capability flags live under `Features`; runtime limits live under `Agent`.

## Authentication

`AiAgentCanvas.Authentication` (Platform) picks schemes by name from
`Authentication:Schemes`, the same way `Provider` picks an LLM backend. Built in:
`ApiKey` for machine callers and `JwtBearer` for any OIDC authority, which covers
Entra ID, Auth0, Okta and Keycloak without a vendor-specific dependency. Add an
`IAgentAuthenticationScheme` to DI and it becomes selectable by name.

That port lives in the Authentication project rather than `Abstractions` on
purpose: every implementation needs ASP.NET Core authentication types, and adding
that framework reference to `Abstractions` would push a web dependency onto every
capability that references it.

Endpoints are protected with `RequireAgentAuthorization(auth, key)` rather than
`RequireAuthorization`, so the `Authentication:AllowAnonymous` list is honoured in
one place. Keys in use: `agui`, `a2a`, `devui`, `notifications`, `webhooks`,
`health`, `runs`, `connections`, `connectors`. Two routes carry no endpoint
authorization on purpose, because the caller is another service and not one of ours: the
OAuth callback (`MapConnectionCallback`, protected by encrypted time-limited state) and
the connector webhook (`MapConnectorWebhook`, protected by the sender's own signature). Authentication is off by default and the Host logs a prominent warning
on every start while it is.

## Runtime invariants

These exist because an agent without them fails in ways that produce no error:

- **Every prompt is counted and budgeted.** `ContextBudgetChatClient` counts with the model's tokenizer, enforces per-component ceilings (`Agent:ContextBudget`), and compacts history with a summarizer rather than letting the provider truncate the prompt silently.
- **Every run has an exit condition.** `LoopGuardChatClient` enforces a tool-round cap, a token budget, repeated-action detection and stagnation detection (`Agent:LoopGuard`). When one trips it withdraws the tools and instructs the model to report what it has.
- **Evaluation lives in source, not in a database.** `tests/AiAgentCanvas.EvalTests` holds checked-in cases graded by `Microsoft.Extensions.AI.Evaluation.Quality`, so a regression fails the build. Evaluators are picked per case: applying Fluency to a case that demands a terse list fails a correct answer. The suite skips itself when no model is configured, so a fresh clone stays green.
- **Filesystem and shell access are allowlisted, and empty means deny.** `SystemToolOptions.AllowedPaths` and `AllowedCommands` both deny everything when empty. Commands run without a shell.
- **Governance approval lists reference real tool names.** `Security:ApprovalRequiredTools` must match the names tools register under (`SystemToolNames`), or it silently protects nothing.
- **Every model call is counted and priced.** `CostTrackingChatClient` records
  `aiagentcanvas.model.{calls,tokens,cost}`, streaming included. Rates live in
  `Agent:Pricing`; an unpriced model reports tokens rather than zero spend.
- **Background producers have consumers.** The scheduler has `ScheduledTaskRunner`; event triggers have `TriggerDispatchService`; the trigger queue is bounded and durable (`TriggerStore`), with at-least-once delivery, dedupe keys, backoff and a dead-letter state. A producer without a consumer is a feature that accepts work and never does it.
- **Every unattended run is recorded and limited.** `RunTracking.RunAsync` writes a ledger record for scheduled tasks, triggers, handoffs and jobs. `Agent:Budgets` refuses a run once an agent, trigger or total daily limit is spent, measured from the ledger, and refuses to start without `RunLedger` and `Agent:Pricing`. Persona agents use the same pipeline as the default agent (`AgentPipeline`), so a limit applies to delegated work too.
- **Secrets never reach the model, a log or a response.** Credentials are encrypted at rest and leave `ICredentialProvider` only to the connection's HTTP client. A connector gets its HTTP client from `IConnectionContext`, which restricts hosts, refuses redirects and retries only safe methods.
- **Tools that reach people need approval.** A connector tool with risk `Send` or `Destructive` is wrapped in `ApprovalRequiredAIFunction` unless `Connectors:ApprovalMode` is `Audit`. `Security:ApprovalRequiredTools` is a separate mechanism and blocks the tool without asking.
