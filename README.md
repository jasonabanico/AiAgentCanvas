# AI Agent Canvas

A multi-agent enterprise copilot framework built with .NET 9, Microsoft Agent Framework (MAF), CopilotKit, and the AG-UI protocol. Compose specialized AI agents that reason, plan, and act through a shared tool registry.

## Architecture

```
Frontend (Next.js + CopilotKit)
        │ AG-UI Protocol (SSE)
        ▼
ASP.NET Core Backend
├── Core ──────────── MAF ChatClientAgent, AG-UI endpoint, DI
├── AgentData ─────── personas, workflows, guardrails, entities
├── Skills ────────── skill registry, MCP connections
├── Scheduler ─────── Hangfire-based recurring tasks
├── Security ──────── governance, prompt injection detection
├── SystemTools ───── file I/O, shell execution (sandboxed)
└── Custom/ ───────── your agents, tools, and data connections
        │
        ▼
Azure AI Foundry (AzureOpenAIClient)
```

## Project Structure

```
src/
├── AiAgentCanvas.Abstractions/     # Shared interfaces and models
├── AiAgentCanvas.Core/             # MAF agent, AG-UI endpoint, DI extensions
├── AiAgentCanvas.AgentData/        # Personas, workflows, guardrails, entities, context, profiles
├── AiAgentCanvas.Skills/           # Skill store, MCP connections, skill authoring
├── AiAgentCanvas.Scheduler/        # Hangfire scheduled tasks
├── AiAgentCanvas.Security/         # Agent Governance Toolkit integration
├── AiAgentCanvas.SystemTools/      # File and shell tools (sandboxed)
├── AiAgentCanvas.Notifications/    # Notification sink
├── AiAgentCanvas.Web/              # Composition root (Program.cs)
└── Custom/                         # Your extensions
    ├── HelloWorldAgent/            # Starter example: tool provider + DI registration
    ├── MCP.MarketData/             # SEC EDGAR + Yahoo Finance tools
    └── VectorStore.Sqlite/         # SQLite vector store for RAG
frontend/                           # Next.js + CopilotKit chat UI
docs/                               # GitHub Pages documentation site
```

The `Custom/` folder is where you add your own tool providers, MCP connections, and agent configurations without touching the framework.

## Quick Start

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Node.js 22+](https://nodejs.org/)
- An Azure OpenAI deployment (or any OpenAI-compatible endpoint)
- No additional API keys needed (Yahoo Finance and SEC EDGAR are free)

### 1. Configure

Create `src/AiAgentCanvas.Web/appsettings.Development.json`:

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
cd src/AiAgentCanvas.Web
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

See `Custom/HelloWorldAgent/` for a complete working example. A custom agent pairs **domain tools** with a **persona** that tells the LLM how to use them.

### 1. Create a tool provider

```csharp
public sealed class HelloWorldToolProvider
{
    public IReadOnlyList<AITool> GetTools() =>
    [
        AIFunctionFactory.Create(Greet, "hello_greet", "Greet a user by name"),
        AIFunctionFactory.Create(RollDice, "hello_roll_dice", "Roll dice"),
    ];

    private static string Greet(string name) => /* ... */;
    private static string RollDice(int count = 1, int sides = 6) => /* ... */;
}
```

### 2. Create a service extension that registers tools + persona

```csharp
public static IServiceCollection AddHelloWorldAgent(this IServiceCollection services)
{
    // Register tools
    services.AddSingleton<HelloWorldToolProvider>();
    services.AddSingleton<IReadOnlyList<AITool>>(sp =>
        sp.GetRequiredService<HelloWorldToolProvider>().GetTools());

    // Seed a persona — saved to agent-data/personas/ on first startup
    services.AddSingleton<IPersonaSeed>(new PersonaSeed(
        name: "hello-world",
        description: "A friendly demo agent that greets users and rolls dice",
        instructions: """
            You are a friendly demo assistant.
            Use hello_greet, hello_roll_dice, and hello_coin_flip to interact.
            """));

    return services;
}
```

### 3. Wire it in Program.cs

```csharp
builder.Services.AddHelloWorldAgent();
```

Tools are immediately available to the agent. The persona is saved to `agent-data/personas/hello-world.md` on first startup — users can activate it with *"switch to the hello-world persona"*.

## Key Features

- **62 built-in tools** — market data, system tools, scheduling, personas, workflows, guardrails, entities, skills, MCP, file I/O
- **AG-UI streaming** — real-time SSE responses via CopilotKit
- **Personas** — switch agent behavior with custom system prompts
- **Workflows** — define multi-step workflows the agent executes
- **Guardrails** — policy rules that constrain agent behavior
- **Scheduled tasks** — Hangfire-powered recurring agent invocations
- **MCP connections** — connect to external MCP servers at runtime
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
| Security | Agent Governance Toolkit |

## License

MIT
