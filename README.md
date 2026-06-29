# [AI Agent Canvas](https://jasonabanico.github.io/AiAgentCanvas/)

A multi-agent enterprise copilot platform built with .NET, Microsoft Agent Framework (MAF), CopilotKit, and the AG-UI protocol. Compose specialized AI agents that reason, plan, and act through a shared tool registry — with inter-agent communication, autonomous goal execution, and runtime governance.

## Architecture

```
Frontend (Next.js + CopilotKit)
        │ AG-UI Protocol (SSE)
        ▼
Orchestrator (ASP.NET Core)
├── AiAgentCanvas SDK ─── Abstractions, Core, AgentData, Skills, Scheduler, Security, etc.
├── Agents ────────────── specialist agents (in-process, separable to out-of-process)
├── DataConnections ───── MCP tools, vector stores
└── IAgentMessaging ───── in-process messaging (swap to gRPC/queue for out-of-process)
        │
        ▼
Azure AI Foundry (AzureOpenAIClient)
```

## Project Structure

```
src/
├── AiAgentCanvas/                  # SDK library (shared by orchestrator and agents)
│   ├── AiAgentCanvas.Abstractions/ # Interfaces, seed contracts, IAgentMessaging
│   ├── AiAgentCanvas.Core/         # MAF agent, AG-UI endpoint, agent registry, handoff
│   ├── AiAgentCanvas.AgentData/    # Personas, context, workflows, entities, profiles, guardrails, goals
│   ├── AiAgentCanvas.Skills/       # Skill store, MCP connections, skill authoring
│   ├── AiAgentCanvas.Scheduler/    # Hangfire scheduled tasks + autonomous execution
│   ├── AiAgentCanvas.Security/     # Agent Governance Toolkit integration
│   ├── AiAgentCanvas.SystemTools/  # File and shell tools (sandboxed)
│   └── AiAgentCanvas.Notifications/# Notification sink
├── Orchestrator/
│   └── AiAgentCanvas.Orchestrator/ # Composition root (Program.cs) — the web host
├── Agents/
│   └── Agent.HelloWorld/           # Starter agent: financial analyst persona
└── DataConnections/
    ├── MCP.HelloWorldData/             # SEC EDGAR + Yahoo Finance tools
    └── VectorStore.Sqlite/         # SQLite vector store for RAG

agent-data/                         # Per-agent runtime data (created on first run)
├── shared/                         # Data inherited by all agents
├── orchestrator/                   # Orchestrator-specific data
└── {agent-name}/                   # Agent-specific data

frontend/                           # Next.js + CopilotKit chat UI
docs/                               # GitHub Pages documentation site
```

Agents start **in-process** (running inside the Orchestrator) but are designed to separate into independent processes later. The separation seam is `IAgentMessaging` — swap `InProcessAgentMessaging` for a gRPC or message-queue implementation and each agent becomes its own deployable service.

## Quick Start

### Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download)
- [Node.js 22+](https://nodejs.org/)
- An Azure OpenAI deployment (or any OpenAI-compatible endpoint)
- No additional API keys needed (Yahoo Finance and SEC EDGAR are free)

### 1. Configure

Create `src/Orchestrator/AiAgentCanvas.Orchestrator/appsettings.Development.json`:

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

### 2. Run the Backend

```bash
cd src/Orchestrator/AiAgentCanvas.Orchestrator
dotnet run
```

### 3. Run the Frontend

```bash
cd frontend
npm install
npm run dev
```

Open `http://localhost:3000`. Try: *"What is the current stock price of AAPL and how has it performed over the last month?"*

## Adding a Custom Agent

See `src/Agents/Agent.HelloWorld/` for a complete working example. A custom agent seeds all the components it needs — persona, context, workflows, entities, user profiles, guardrails, goals, and skills — and references tools from separate data connection projects.

### 1. Create a service extension that seeds components

```csharp
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.DependencyInjection;

public static class HelloWorldServiceExtensions
{
    public static IServiceCollection AddHelloWorldAgent(this IServiceCollection services)
    {
        services.AddSingleton<IPersonaSeed>(new PersonaSeed(
            name: "financial-analyst",
            description: "A financial analyst that uses market data tools",
            instructions: "You are a financial analyst assistant..."));

        // Also: IContextSeed, IWorkflowSeed, IEntitySeed

        services.AddSingleton<IGuardrailSeed>(new GuardrailSeed(
            name: "investment-disclaimer",
            severity: "high", enabled: true,
            rule: "Never provide buy/sell recommendations..."));

        // Also: IGoalSeed, ISkillSeed, IMcpConnectionSeed

        // Declare tool dependencies (validated at startup)
        services.AddSingleton<IToolDependencySeed>(new ToolDependencySeed(
            agentName: "customer-support",
            requiredTools: ["search_kb", "lookup_order"]));

        return services;
    }
}
```

### 2. Wire it in Program.cs

```csharp
builder.Services.AddHelloWorldAgent();
```

All seeded components are saved to disk on first startup (seeds never overwrite manual edits). The tools referenced in the persona (`stock_quote`, `stock_history`, `edgar_company_facts`) come from the `MCP.HelloWorldData` data connection registered separately.

**Agents and data connections are separate projects.** Agents define *how* the LLM behaves (via personas, context, workflows, entities, guardrails, skills). Data connections define *what* it can do (via tools). This separation lets multiple agents share the same tools.

## Key Features

- **AG-UI streaming** — real-time token-by-token SSE responses via CopilotKit
- **Personas** — switch agent behavior with custom system prompts
- **Workflows** — define multi-step procedures the agent executes
- **Guardrails** — policy rules that constrain agent behavior
- **70+ built-in tools** — market data, personas, context, workflows, entities, guardrails, goals, skills, MCP, system tools, and more
- **MCP connections** — connect to external MCP servers at runtime for additional tools
- **Scheduled tasks** — Hangfire-powered recurring agent invocations
- **Autonomous execution** — goal-driven work queue with a Hangfire job that claims, executes, and reports on work items without user input
- **Inter-agent communication** — agent registry, mailbox messaging, and synchronous handoff between named agents built from personas
- **Security** — OWASP LLM Top 10 coverage via Agent Governance Toolkit

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Frontend | Next.js 15, React 19, CopilotKit |
| Protocol | AG-UI (Server-Sent Events) |
| Backend | ASP.NET Core 9, Minimal APIs |
| Agent Framework | Microsoft Agent Framework (MAF) |
| AI | Azure AI Foundry (`Azure.AI.OpenAI`) |
| Data | SEC EDGAR (free), Yahoo Finance (free) |
| Scheduling | Hangfire with SQLite |
| Autonomous | Goal Store, Work Queue (SQLite), AutonomousAgentJob |
| Inter-Agent | Agent Registry, Mailbox (SQLite), Handoff |
| Security | Agent Governance Toolkit |

## License

MIT
