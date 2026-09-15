# [AI Agent Canvas](https://jasonabanico.github.io/AiAgentCanvas/)

A multi-agent enterprise copilot platform built with .NET 9, Microsoft Agent Framework (MAF), and the AG-UI protocol. Compose specialized AI agents that reason, plan, and act through a shared tool registry, with inter-agent communication, scheduled and event-driven execution, and runtime governance.

## Architecture

```
Frontend (Next.js 15 + React 19, AG-UI client)
        │ AG-UI Protocol (SSE)
        ▼
Host (ASP.NET Core 9) ─ composition root
├── Platform ──────────── Abstractions, Orchestration, Security, Storage
├── Capabilities ──────── opt-in feature modules behind Features:* flags
├── AgentData ─────────── personas, context, entities, guardrails, profiles, workflows
├── Agents ────────────── specialist agents (in-process, separable to out-of-process)
├── DataConnections ───── tool providers and vector stores
└── Providers ─────────── swappable LLM backends selected by the Provider key
        │
        ▼
Azure AI Foundry | Databricks | Snowflake Cortex
```

The runtime wraps the model in a chat-client pipeline before the agent ever sees it:

```
LoopGuard ─ Reflection ─ Audit ─ ModelRouter ─ ContextBudget ─ ToolDedupe ─ provider
```

`ContextBudget` counts the prompt with the model's own tokenizer and compacts history before the provider truncates it silently. `LoopGuard` holds the run's exit conditions: a tool-round cap, a token budget, repeat detection, and stagnation detection. When one trips it withdraws the tools and asks the model to report what it has, which is what actually ends the loop. Both are configured under `Agent:` in `appsettings.json`.

## Project Structure

```
src/
├── Platform/
│   ├── AiAgentCanvas.Abstractions/   # seed contracts, cron, telemetry, messaging interfaces
│   ├── AiAgentCanvas.Orchestration/  # MAF wiring, AG-UI endpoint, registry, handoff, chat pipeline
│   ├── AiAgentCanvas.Security/       # Agent Governance Toolkit + Purview
│   └── AiAgentCanvas.Storage/
├── Capabilities/                     # Rag, Scheduling, Skills, Notifications, SystemTools,
│                                     # EpisodicMemory, AuditLog, EventTriggers, ComputerUse, Evaluation
├── AgentData/                        # Personas, Context, Entities, Guardrails, Profiles, Workflows
├── Agents/
│   └── Agent.FinancialAnalyst/       # sample: financial analysis persona + tool declarations
├── DataConnections/
│   ├── DataConnection.MarketData/    # Yahoo Finance + SEC EDGAR
│   ├── DataConnection.VectorStore.Sqlite/
│   ├── DataConnection.VectorSearch.Databricks/
│   ├── DataConnection.VectorSearch.Snowflake/
│   └── AiAgentCanvas.Storage.Sqlite/
├── Providers/                        # AzureAIFoundry, Databricks, Snowflake
└── Host/
    └── AiAgentCanvas.Host/           # composition root (Program.cs)

tests/AiAgentCanvas.Tests/            # cron, token counting, context budget, loop guard,
                                      # system-tool sandboxing, episodic memory
agent-data/                           # per-agent runtime data (created on first run)
frontend/                             # Next.js AG-UI chat client
docs/                                 # GitHub Pages documentation site
```

Agents start **in-process** but are designed to separate into independent services. The seam is `IAgentMessaging`: swap `InProcessAgentMessaging` for a gRPC or queue implementation and each agent becomes its own deployable.

## Quick Start

### Prerequisites

- [.NET SDK 9](https://dotnet.microsoft.com/download)
- [Node.js 22+](https://nodejs.org/)
- An Azure OpenAI deployment, a Databricks serving endpoint, or a Snowflake Cortex account
- No additional API keys needed for the included samples (Yahoo Finance and SEC EDGAR are free)

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
- `JudgeDeploymentName` gives the Evaluation capability a model distinct from the one under test. Without it, evaluation grades the model with itself and says so in its output.

### 2. Run the Backend

```bash
cd src/Host/AiAgentCanvas.Host
dotnet run
```

### 3. Run the Frontend

```bash
cd frontend
npm install
npm run dev
```

Open `http://localhost:3000`. Try: *"What is the current stock price of AAPL and how has it performed over the last month?"*

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
- **Episodic memory** — importance-gated writes, embedding-ranked recall, importance-weighted decay
- **RAG** — hybrid retrieval, chunk overlap, LLM reranking, cited sources
- **MCP connections** — connect to external MCP servers at runtime, with auth, issuer checks, health pings, and reconnect
- **Scheduled tasks** — cron-scheduled agent runs executed by a hosted runner, with per-task timeouts
- **Event triggers** — cron, file-watch, and webhook triggers dispatched to the agent through a bounded queue
- **Inter-agent communication** — agent registry with A2A agent cards, mailbox messaging, synchronous handoff
- **Evaluation** — LLM-as-judge against the full agent, graded by a separate model, with task success rate, tool-use accuracy, and trajectory efficiency
- **Observability** — OpenTelemetry spans per tool call and metrics for tool outcomes, run terminations, context pressure, and eval results
- **Security** — Agent Governance Toolkit, prompt-injection detection, approval gates on side-effecting tools, allowlisted filesystem and shell access

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Frontend | Next.js 15, React 19, hand-rolled AG-UI SSE client |
| Protocol | AG-UI (Server-Sent Events), A2A (JSON over HTTP) |
| Backend | ASP.NET Core 9, Minimal APIs |
| Agent Framework | Microsoft Agent Framework (MAF) |
| AI | Azure AI Foundry, Databricks Foundation Model APIs, Snowflake Cortex |
| Tokenizer | `Microsoft.ML.Tokenizers` (cl100k / o200k) |
| Data | MCP tool providers (sample: SEC EDGAR, Yahoo Finance) |
| Storage | SQLite (chat history, vectors, episodes, tasks, audit, evals) |
| Scheduling | Hosted `BackgroundService` with a 5-field cron evaluator |
| Inter-Agent | Agent Registry, in-process mailbox, handoff |
| Observability | OpenTelemetry, exported through Azure Monitor when configured |
| Security | Agent Governance Toolkit, Microsoft Purview |
| Tests | xUnit |

## License

MIT
