# [AI Agent Canvas](https://jasonabanico.github.io/AiAgentCanvas/)

A multi-agent enterprise copilot platform built with .NET 10, Microsoft Agent Framework (MAF), and the AG-UI protocol. Compose specialized AI agents that reason, plan, and act through a shared tool registry, with inter-agent communication, scheduled and event-driven execution, and runtime governance.

## Architecture

```
Frontend (Next.js 15 + React 19, AG-UI client)
        │ AG-UI Protocol (SSE)
        ▼
Host (ASP.NET Core 10) ─ composition root
├── Platform ──────────── Abstractions, Orchestration, Security, Authentication, Connections, Connectors
├── Capabilities ──────── opt-in feature modules behind Features:* flags
├── AgentData ─────────── personas, context, entities, guardrails, profiles, workflows
├── Agents ────────────── specialist agents (plugins, in-process, separable to out-of-process)
├── Connectors ────────── Twilio SMS and an MCP adapter (Gmail), one instance per stored connection
├── DataConnections ───── tool providers, storage adapters and vector stores
└── Providers ─────────── swappable LLM backends selected by the Provider key
        │
        ▼
Azure AI Foundry | Databricks | Snowflake Cortex | a local model server
```

The runtime wraps the model in a chat-client pipeline before the agent ever sees it:

```
LoopGuard ─ Reflection ─ Audit ─ ModelRouter ─ ToolSelection ─ ContextBudget ─ CostTracking ─ ToolDedupe ─ provider
```

`ContextBudget` counts the prompt with the model's own tokenizer and compacts history before the provider truncates it silently. `LoopGuard` holds the run's exit conditions: a tool-round cap, a token budget, repeat detection, and stagnation detection. When one trips it withdraws the tools and asks the model to report what it has, which is what actually ends the loop. Both are configured under `Agent:` in `appsettings.json`. `CostTracking` prices each model call from `Agent:Pricing`. `ToolSelection` is off by default. Turned on, it narrows a long tool list to the tools that fit the user's message, and a cap on the size of one tool result keeps a single large answer from filling the prompt. Persona agents, scheduled runs, triggers and orchestrations use the same pipeline and tool wrapping as the default agent, so a limit or a governance rule applies to delegated work too.

## Project Structure

```
src/
├── Platform/
│   ├── AiAgentCanvas.Abstractions/   # seed contracts, cron, telemetry, messaging interfaces
│   ├── AiAgentCanvas.Orchestration/  # MAF wiring, AG-UI endpoint, registry, handoff, chat pipeline
│   ├── AiAgentCanvas.Security/       # Agent Governance Toolkit + Purview
│   ├── AiAgentCanvas.Authentication/ # API key and JWT bearer schemes
│   ├── AiAgentCanvas.Connections/    # encrypted credential store, OAuth, token refresh
│   └── AiAgentCanvas.Connectors/     # connector contracts, host, guarded HTTP client, webhooks
├── Capabilities/                     # Rag, Scheduling, Skills, Notifications, SystemTools,
│                                     # EpisodicMemory, AuditLog, EventTriggers, ComputerUse,
│                                     # RunLedger, Jobs
├── Connectors/                       # Connector.TwilioSms, Connector.Mcp (Gmail and any MCP server)
├── AgentData/                        # Personas, Context, Entities, Guardrails, Profiles, Workflows
├── Agents/
│   └── Agent.FinancialAnalyst/       # sample: financial analysis persona + tool declarations
├── DataConnections/
│   ├── DataConnection.MarketData/    # Yahoo Finance + SEC EDGAR
│   ├── DataConnection.VectorStore.Sqlite/
│   ├── DataConnection.VectorSearch.Databricks/
│   ├── DataConnection.VectorSearch.Snowflake/
│   └── DataConnection.Storage.Sqlite/ # scheduled task store
├── Providers/                        # AzureAIFoundry, Databricks, Snowflake, Local
└── Host/
    └── AiAgentCanvas.Host/           # composition root (Program.cs)

tests/AiAgentCanvas.Tests/            # cron, token counting, context budget, loop guard, run ledger,
                                      # budgets, trigger queue, jobs, credentials, connectors
tests/AiAgentCanvas.EvalTests/        # checked-in model evaluations, run in CI
agent-data/                           # per-agent runtime data (created on first run)
frontend/                             # Next.js AG-UI chat client
docs/                                 # GitHub Pages documentation site
```

Agents start **in-process** but are designed to separate into independent services. The seam is `IAgentMessaging`: swap `InProcessAgentMessaging` for a gRPC or queue implementation and each agent becomes its own deployable.

## Quick Start

### Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Node.js 22+](https://nodejs.org/)
- An Azure OpenAI deployment, a Databricks serving endpoint, a Snowflake Cortex account, or a local model server such as Ollama (see [Running Fully Local](docs/guide-13-running-fully-local.md))
- No additional API keys needed for the sample agent's data tools (Yahoo Finance and SEC EDGAR are free)

### 1. Configure

Create `src/Host/AiAgentCanvas.Host/appsettings.Development.json`:

```json
{
  "AIFoundry": {
    "Endpoint": "https://your-resource.openai.azure.com",
    "Key": "your-api-key",
    "DeploymentName": "gpt-4o",
    "UseAzureCredential": false
  }
}
```

Two optional deployments are worth setting:

- `EconomyDeploymentName` gives the cost-aware router a cheaper model for low-complexity turns and gives history compaction a cheap summarizer.
- `JudgeDeploymentName` gives the evaluation suite in `tests/AiAgentCanvas.EvalTests` a model distinct from the one under test. Without it, the suite skips itself.

### 2. Turn on what you want to try

All capabilities are off by default, so a fresh start is a bare chat agent. Add the flags you want to `appsettings.Development.json`. This set enables the persona, context and guardrail stores, skills, workflows, the run ledger, and the sample financial analyst agent with its market data tools:

```json
{
  "Features": {
    "Personas": true,
    "Context": true,
    "Guardrails": true,
    "Skills": true,
    "Workflows": true,
    "InterAgentCommunication": true,
    "RunLedger": true
  },
  "Agents": { "FinancialAnalyst": { "Enabled": true } },
  "DataConnections": { "MarketData": { "Enabled": true } }
}
```

The agent and data connection are plugins. A normal solution build copies them into the Host's `plugins/` folder.

### 3. Run the Backend

```bash
cd src/Host/AiAgentCanvas.Host
dotnet run
```

### 4. Run the Frontend

```bash
cd frontend
npm install
npm run dev
```

Open `http://localhost:3000`. The backend listens on `http://localhost:5149`, and the dev server forwards `/api` requests to it. Try: *"What is the current stock price of AAPL and how has it performed over the last month?"*

### Tests

```bash
dotnet test tests/AiAgentCanvas.Tests/AiAgentCanvas.Tests.csproj
```

## Adding a Custom Agent

See `src/Agents/Agent.FinancialAnalyst/` for a complete working example. An agent seeds the components it needs (persona, context, workflows, entities, profiles, guardrails, skills) and references tools by name from separate data connection projects.

### 1. Create a service extension that seeds components

```csharp
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.DependencyInjection;

public static class FinancialAnalystServiceExtensions
{
    public static IServiceCollection AddFinancialAnalystAgent(this IServiceCollection services)
    {
        services.AddSingleton<IPersonaSeed>(new PersonaSeed(
            name: "financial-analyst",
            description: "A financial analyst that uses market data tools",
            instructions: "You are a financial analyst assistant..."));

        // Also: IContextSeed, IWorkflowSeed, IEntitySeed, IUserProfileSeed

        services.AddSingleton<IGuardrailSeed>(new GuardrailSeed(
            name: "investment-disclaimer",
            severity: "high", enabled: true,
            rule: "Never provide buy/sell recommendations..."));

        // Also: ISkillSeed, IMcpConnectionSeed

        // Scope the agent to specific tools (validated at startup)
        services.AddSingleton<IAgentToolsSeed>(new AgentToolsSeed(
            agentName: "financial-analyst",
            toolNames: ["stock_quote", "stock_history", "edgar_company_facts"]));

        return services;
    }
}
```

### 2. Wire it in

Either reference the project and call the extension from `Program.cs`, or drop the built output into `plugins/` and let `AddServiceModules` discover it by reflection. The Host never references a plugin assembly directly.

Seeded components are written to disk on first startup and never overwrite manual edits.

**Agents and data connections are separate projects.** Agents define *how* the LLM behaves. Data connections define *what* it can do. Multiple agents can share the same tools.

## Key Features

- **AG-UI streaming** — token-by-token SSE, with tool indicators, step tracking, reasoning panes, and approval gates
- **Context budget** — exact token counting, per-component ceilings, and summarizing compaction before the provider truncates
- **Loop termination** — round cap, token budget, repeated-action detection, and stagnation detection, with escalation instead of iteration
- **Personas** — switch agent behavior with custom system prompts
- **Workflows** — sequential, concurrent, and declarative (YAML) multi-agent workflows through MAF
- **Guardrails** — policy rules that constrain agent behavior
- **Episodic memory** — importance-gated writes, embedding-ranked recall, importance-weighted decay, merging of near-duplicate episodes, a refresh when an episode is recalled, and endpoints and a tool to list and delete what is stored
- **RAG** — document ingestion with versions and expiry, hybrid retrieval fused by rank, LLM reranking, cited sources, and a `rag_search` tool that lets the agent decide when and what to look up
- **MCP connections** — connect to external MCP servers at runtime, with auth, issuer checks, health pings, and reconnect
- **Scheduled tasks** — cron-scheduled agent runs executed by a hosted runner, with per-task timeouts
- **Event triggers** — cron, file-watch, webhook and connector triggers held in a durable SQLite queue with deduplication, retry with backoff, and a dead-letter state
- **Run ledger** — one record per unattended run: source, tool calls, tokens, cost, and how it ended, with a Runs tab in the UI
- **Spend limits** — per-run and daily limits per agent, per trigger and in total, measured from the ledger
- **Jobs** — deterministic scheduled work that runs without a model
- **Typed output** — answers checked against a JSON Schema and retried with the specific errors until they fit
- **Vision** — image tools with allowlisted folders and hosts, and typed extraction from an image
- **Orchestration** — group chat, handoff, Magentic, sequential, concurrent and maker-checker review runs, with checkpoints and a human sign-off step that only a person can answer
- **Local models** — an OpenAI-compatible provider for Ollama, llama.cpp, LM Studio and vLLM, with a guard that refuses a public endpoint
- **MCP server** — serves a chosen set of tools to outside clients, with approval-gated tools excluded and every call recorded
- **Connections and connectors** — encrypted credentials, OAuth with refresh, and connectors with risk-tagged tools, approval for sends, signed webhooks and events (Twilio SMS and Gmail through MCP ship in the box)
- **Inter-agent communication** — agent registry with A2A agent cards, mailbox messaging, synchronous handoff
- **Evaluation** — checked-in cases graded by `Microsoft.Extensions.AI.Evaluation.Quality`, run in CI, so a regression fails the build
- **Observability** — OpenTelemetry spans per tool call and metrics for tool outcomes, run terminations, context pressure, and eval results
- **Security** — Agent Governance Toolkit, prompt-injection detection, approval gates on side-effecting tools, allowlisted filesystem and shell access

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Frontend | Next.js 15, React 19, hand-rolled AG-UI SSE client |
| Protocol | AG-UI (Server-Sent Events), A2A (JSON over HTTP) |
| Backend | ASP.NET Core 10, Minimal APIs |
| Agent Framework | Microsoft Agent Framework (MAF) |
| AI | Azure AI Foundry, Databricks Foundation Model APIs, Snowflake Cortex, local OpenAI-compatible servers |
| Tokenizer | `Microsoft.ML.Tokenizers` (cl100k / o200k) |
| Data | Tool providers and connectors (sample: SEC EDGAR, Yahoo Finance, Twilio SMS, MCP servers) |
| Storage | SQLite (chat history, vectors, episodes, tasks, audit, triggers, runs, connections, orchestrations) |
| Scheduling | Hosted `BackgroundService` with a 5-field cron evaluator |
| Inter-Agent | Agent Registry, in-process mailbox, handoff |
| Observability | OpenTelemetry, exported through Azure Monitor when configured |
| Security | Agent Governance Toolkit, Microsoft Purview |
| Tests | xUnit |

## License

MIT
