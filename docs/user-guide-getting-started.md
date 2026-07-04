> [User Guide](user-guide.md) > Getting Started

# User Guide: Getting Started

This guide walks you through installing, configuring, and running AI Agent Canvas for the first time.

## Prerequisites

Before you begin, make sure you have the following installed:

- **.NET SDK** -- [Download from Microsoft](https://dotnet.microsoft.com/download/dotnet/9.0)
- **Node.js 18+** -- [Download from nodejs.org](https://nodejs.org/)
- **Azure AI Foundry account** (or any OpenAI-compatible endpoint) -- You need an endpoint URL, an API key, and a model deployment name.

Verify your installations:

```
dotnet --version    # Should print 9.x
node --version      # Should print v18.x or higher
npm --version       # Should print 9.x or higher
```

## Clone the Repository

```
git clone <your-repo-url> AgentOpsHub
cd AgentOpsHub
```

## Build the Backend

From the repository root, restore packages and build the solution:

```
dotnet build AiAgentCanvas.sln
```

A successful build compiles the core platform, agents, MCP connections, scheduler, skills engine, and the web host.

## Configure Your AI Endpoint

Open `src/Orchestrator/AiAgentCanvas.Orchestrator/appsettings.json` and fill in the `AIFoundry` section with your endpoint details:

```json
{
  "AIFoundry": {
    "Endpoint": "https://your-resource.openai.azure.com/",
    "Key": "your-api-key-here",
    "DeploymentName": "gpt-4o",
    "EmbeddingDeploymentName": "",
    "UseAzureCredential": false
  }
}
```

| Field | Description |
|-------|-------------|
| `Endpoint` | Your Azure AI Foundry (or OpenAI-compatible) endpoint URL. |
| `Key` | The API key for authentication. |
| `DeploymentName` | The model deployment to use (e.g., `gpt-4o`). |
| `EmbeddingDeploymentName` | Optional. Embedding model for RAG (e.g., `text-embedding-3-small`). Leave empty to disable RAG. |
| `UseAzureCredential` | Set to `true` to use Azure Managed Identity instead of an API key. |

See [Configuration](user-guide-configuration.md) for the full settings reference.

## Run the Backend

Start the .NET backend server:

```
cd src/Orchestrator/AiAgentCanvas.Orchestrator
dotnet run
```

The backend starts on `http://localhost:5000` by default. You should see log output confirming that the agent platform, scheduler, and tool providers have loaded.

## Run the Frontend

In a separate terminal, install dependencies and start the Next.js development server:

```
cd frontend
npm install
npm run dev
```

The frontend starts on `http://localhost:3000`.

## Open the Chat Interface

Open your browser and navigate to **http://localhost:3000**. You will see the CopilotKit chat interface.

## Your First Conversation

Type a message in the chat input and press Enter. The agent processes your message, and you will see the response stream in token by token.

Try these to explore the main features:

1. **Ask a question** -- Type "What can you help me with?" to see the agent describe its capabilities.
2. **Use a built-in tool** -- Type "Get me a stock quote for AAPL" to see the agent call the `stock_quote` tool and return live data.
3. **Create a persona** -- Type "Create a persona called Analyst with instructions to focus on data-driven insights." See [Personas](user-guide-personas.md) for details.
4. **Schedule a task** -- Type "Schedule a recurring task every hour to check the S&P 500 price." See [Scheduling](user-guide-scheduling.md) for details.
5. **Create a workflow** -- Type "Create a workflow called Morning Briefing that gets stock quotes for AAPL, MSFT, and GOOGL." See [Workflows](user-guide-workflows.md) for details.

## Project Structure at a Glance

| Folder | Purpose |
|--------|---------|
| `src/AiAgentCanvas/` | SDK library projects (Core, AgentData, Security, Skills, etc.) |
| `src/Orchestrator/AiAgentCanvas.Orchestrator/` | Composition root -- wires everything together |
| `src/Agents/Agent.HelloWorld/` | Starter example: persona seed for market data tools |
| `src/DataConnections/MCP.HelloWorldData/` | Sample data connection (SEC EDGAR + Yahoo Finance) |
| `src/DataConnections/VectorStore.Sqlite/` | SQLite vector store for RAG |
| `frontend/` | Next.js + CopilotKit chat UI |
| `agent-data/orchestrator/` | Orchestrator's runtime data (personas, context, workflows, entities, guardrails) |
| `agent-data/shared/` | Shared data accessible to all agents |

## Next Steps

- [Configuration](user-guide-configuration.md) -- Detailed settings reference
- [Chat Interface](user-guide-chat.md) -- How the chat UI works
- [Skills & Tools](user-guide-skills.md) -- Built-in and custom tools
- [Security](user-guide-security.md) -- Governance policies and rate limiting

---
