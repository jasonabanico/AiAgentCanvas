# User Guide

## Table of Contents

- [Getting Started](#getting-started)
  - [Prerequisites](#prerequisites)
  - [Clone the Repository](#clone-the-repository)
  - [Build the Backend](#build-the-backend)
  - [Configure Your AI Endpoint](#configure-your-ai-endpoint)
  - [Run the Backend](#run-the-backend)
  - [Run the Frontend](#run-the-frontend)
  - [Open the Chat Interface](#open-the-chat-interface)
  - [Your First Conversation](#your-first-conversation)
  - [Project Structure at a Glance](#project-structure-at-a-glance)
  - [Next Steps](#next-steps)
- [Configuration](#configuration)
  - [Configuration File Structure](#configuration-file-structure)
  - [AIFoundry Section](#aifoundry-section)
  - [Security Section](#security-section)
  - [VectorStore Section](#vectorstore-section)
  - [Logging Section](#logging-section)
  - [Environment-Specific Configuration](#environment-specific-configuration)
  - [Environment Variables](#environment-variables)
  - [Frontend Configuration](#frontend-configuration)
  - [Runtime Data Directories](#runtime-data-directories)
- [Chat Interface](#chat-interface)
  - [Overview](#overview)
  - [Sending Messages](#sending-messages)
  - [Streaming Responses](#streaming-responses)
  - [Tool Calls](#tool-calls)
  - [Conversation Context](#conversation-context)
  - [Notifications](#notifications)
  - [Tips for Effective Conversations](#tips-for-effective-conversations)
  - [Troubleshooting](#troubleshooting)
  - [Architecture Note](#architecture-note)
- [Personas](#personas)
  - [What Is a Persona?](#what-is-a-persona)
  - [Creating a Persona](#creating-a-persona)
  - [Listing Personas](#listing-personas)
  - [Switching Personas](#switching-personas)
  - [Reading a Persona](#reading-a-persona)
  - [Updating a Persona](#updating-a-persona)
  - [Deleting a Persona](#deleting-a-persona)
  - [How Personas Work Internally](#how-personas-work-internally)
  - [Example Personas](#example-personas)
- [Skills & Tools](#skills--tools)
  - [Built-In Tools](#built-in-tools)
  - [What Are Skills?](#what-are-skills)
  - [Creating Skills](#creating-skills)
  - [Listing and Running Skills](#listing-and-running-skills)
  - [Removing Skills](#removing-skills)
  - [Skill Authoring (Markdown-Based)](#skill-authoring-markdown-based)
  - [Skill Registry (Catalog)](#skill-registry-catalog)
  - [Connecting External MCP Servers](#connecting-external-mcp-servers)
  - [How Tools Are Registered](#how-tools-are-registered)
  - [Inter-Agent Communication Tools](#inter-agent-communication-tools)
- [Scheduling](#scheduling)
  - [What Scheduled Tasks Do](#what-scheduled-tasks-do)
  - [Creating a Recurring Task](#creating-a-recurring-task)
  - [Creating a One-Time Task](#creating-a-one-time-task)
  - [Viewing Scheduled Tasks](#viewing-scheduled-tasks)
  - [Getting Task Results](#getting-task-results)
  - [Removing a Scheduled Task](#removing-a-scheduled-task)
  - [How It Works](#how-it-works)
  - [The Hangfire Dashboard](#the-hangfire-dashboard)
  - [Autonomous Execution Mode](#autonomous-execution-mode)
- [Workflows](#workflows)
  - [What Is a Workflow?](#what-is-a-workflow)
  - [Creating a Workflow](#creating-a-workflow)
  - [Listing Workflows](#listing-workflows)
  - [Reading a Workflow](#reading-a-workflow)
  - [Running a Workflow](#running-a-workflow)
  - [Deleting a Workflow](#deleting-a-workflow)
  - [Workflow File Format](#workflow-file-format)
  - [Example Workflows](#example-workflows)
  - [Combining Workflows with Scheduling](#combining-workflows-with-scheduling)
- [Security](#security)
  - [Governance Policy Overview](#governance-policy-overview)
  - [Rate Limiting](#rate-limiting)
  - [Prompt Injection Detection](#prompt-injection-detection)
  - [Tool-Call Governance](#tool-call-governance)
  - [Guardrails (User-Defined Safety Rules)](#guardrails-user-defined-safety-rules)
  - [Security Headers](#security-headers)
  - [Audit Logging](#audit-logging)
  - [Security Checklist for Production](#security-checklist-for-production)

---

## Getting Started

This guide walks you through installing, configuring, and running AI Agent Canvas for the first time.

### Prerequisites

Before you begin, make sure you have the following installed:

- **.NET 9 SDK** -- [Download from Microsoft](https://dotnet.microsoft.com/download/dotnet/9.0)
- **Node.js 18+** -- [Download from nodejs.org](https://nodejs.org/)
- **Azure AI Foundry account** (or any OpenAI-compatible endpoint) -- You need an endpoint URL, an API key, and a model deployment name.

Verify your installations:

```
dotnet --version    # Should print 9.x
node --version      # Should print v18.x or higher
npm --version       # Should print 9.x or higher
```

### Clone the Repository

```
git clone <your-repo-url> AgentOpsHub
cd AgentOpsHub
```

### Build the Backend

From the repository root, restore packages and build the solution:

```
dotnet build AiAgentCanvas.sln
```

A successful build compiles the core platform, agents, MCP connections, scheduler, skills engine, and the web host.

### Configure Your AI Endpoint

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

See [Configuration](#configuration) for the full settings reference.

### Run the Backend

Start the .NET backend server:

```
cd src/Orchestrator/AiAgentCanvas.Orchestrator
dotnet run
```

The backend starts on `http://localhost:5000` by default. You should see log output confirming that the agent platform, scheduler, and tool providers have loaded.

### Run the Frontend

In a separate terminal, install dependencies and start the Next.js development server:

```
cd frontend
npm install
npm run dev
```

The frontend starts on `http://localhost:3000`.

### Open the Chat Interface

Open your browser and navigate to **http://localhost:3000**. You will see the CopilotKit chat interface.

### Your First Conversation

Type a message in the chat input and press Enter. The agent processes your message, and you will see the response stream in token by token.

Try these to explore the main features:

1. **Ask a question** -- Type "What can you help me with?" to see the agent describe its capabilities.
2. **Use a built-in tool** -- Type "Get me a stock quote for AAPL" to see the agent call the `stock_quote` tool and return live data.
3. **Create a persona** -- Type "Create a persona called Analyst with instructions to focus on data-driven insights." See [Personas](#personas) for details.
4. **Schedule a task** -- Type "Schedule a recurring task every hour to check the S&P 500 price." See [Scheduling](#scheduling) for details.
5. **Create a workflow** -- Type "Create a workflow called Morning Briefing that gets stock quotes for AAPL, MSFT, and GOOGL." See [Workflows](#workflows) for details.

### Project Structure at a Glance

| Folder | Purpose |
|--------|---------|
| `src/AiAgentCanvas/` | SDK library projects (Core, AgentData, Security, Skills, etc.) |
| `src/Orchestrator/AiAgentCanvas.Orchestrator/` | Composition root -- wires everything together |
| `src/Agents/Agent.HelloWorld/` | Starter example: persona seed for market data tools |
| `src/DataConnections/MCP.HelloWorldData/` | SEC EDGAR + Yahoo Finance data tools |
| `src/DataConnections/VectorStore.Sqlite/` | SQLite vector store for RAG |
| `frontend/` | Next.js + CopilotKit chat UI |
| `agent-data/orchestrator/` | Orchestrator's runtime data (personas, context, workflows, entities, guardrails) |
| `agent-data/shared/` | Shared data accessible to all agents |

### Next Steps

- [Configuration](#configuration) -- Detailed settings reference
- [Chat Interface](#chat-interface) -- How the chat UI works
- [Skills & Tools](#skills--tools) -- Built-in and custom tools
- [Security](#security) -- Governance policies and rate limiting

---

## Configuration

All backend configuration lives in `src/Orchestrator/AiAgentCanvas.Orchestrator/appsettings.json`. This page covers every section and how to customize it for your environment.

### Configuration File Structure

The main configuration file contains these sections:

```json
{
  "Logging": { ... },
  "AllowedHosts": "*",
  "AIFoundry": { ... },
  "Security": { ... },
  "VectorStore": { ... }
}
```

### AIFoundry Section

This section configures the connection to your LLM provider. AI Agent Canvas uses Azure AI Foundry by default but works with any OpenAI-compatible endpoint.

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

| Setting | Required | Description |
|---------|----------|-------------|
| `Endpoint` | Yes | The base URL of your AI endpoint. |
| `Key` | Yes* | API key for authentication. Not required if `UseAzureCredential` is `true`. |
| `DeploymentName` | Yes | The model deployment name (e.g., `gpt-4o`, `gpt-4o-mini`). |
| `EmbeddingDeploymentName` | No | Embedding model for RAG (e.g., `text-embedding-3-small`). When set, enables the SQLite vector store and `RagContextProvider`. Leave empty to disable RAG. |
| `UseAzureCredential` | No | Set to `true` to authenticate with Azure Managed Identity instead of an API key. Defaults to `false`. |

#### Azure AI Foundry Setup

1. Create an Azure AI Foundry resource in the Azure Portal.
2. Deploy a model (e.g., `gpt-4o`) and note the deployment name.
3. Copy the endpoint URL and API key from the resource's Keys and Endpoint page.
4. Paste them into the `AIFoundry` section.

#### Using Azure Managed Identity

For production deployments, use Managed Identity instead of API keys:

```json
{
  "AIFoundry": {
    "Endpoint": "https://your-resource.openai.azure.com/",
    "Key": "",
    "DeploymentName": "gpt-4o",
    "UseAzureCredential": true
  }
}
```

Ensure the hosting identity has the **Cognitive Services OpenAI User** role on the AI Foundry resource.

### Security Section

Controls governance policies and rate limiting.

```json
{
  "Security": {
    "PolicyPath": "governance-policy.yaml",
    "RateLimitPerMinute": 30
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `PolicyPath` | `governance-policy.yaml` | Path to the YAML file defining governance rules. See [Security](#security). |
| `RateLimitPerMinute` | `30` | Maximum API requests per minute per client. Returns HTTP 429 when exceeded. |

### VectorStore Section

Configures the SQLite vector store used for RAG. Only active when `EmbeddingDeploymentName` is set.

```json
{
  "VectorStore": {
    "ConnectionString": "Data Source=vectorstore.db"
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `ConnectionString` | `Data Source=vectorstore.db` | SQLite connection string for the vector store database. |

### Logging Section

Standard .NET logging configuration.

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

Raise the default level to `Debug` for troubleshooting. Set `Microsoft.AspNetCore` to `Information` to see HTTP request logs.

### Environment-Specific Configuration

Create environment-specific overrides using the standard ASP.NET Core pattern:

- **`appsettings.Development.json`** -- Applied when `ASPNETCORE_ENVIRONMENT=Development` (the default during `dotnet run`).
- **`appsettings.Production.json`** -- Applied in production deployments.

Example `appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  },
  "AIFoundry": {
    "Key": "dev-only-key"
  }
}
```

Environment-specific files override values from the base `appsettings.json`. You do not need to repeat settings that stay the same.

### Environment Variables

Any setting can be overridden via environment variables using the `__` (double underscore) separator:

```
export AIFoundry__Key="your-key-here"
export Security__RateLimitPerMinute=60
```

This is useful for CI/CD pipelines and container deployments where you do not want secrets in config files.

### Frontend Configuration

The frontend reads its backend URL from `frontend/.env.local`:

```
NEXT_PUBLIC_BACKEND_URL=http://localhost:5000
```

Change this when the backend runs on a different host or port.

### Runtime Data Directories

The backend creates and uses these directories at runtime (relative to the working directory):

Agent data uses per-agent directories under `agent-data/`. Each agent directory contains `agent/` (system-managed) and `user/` (hand-written, read-only to system) subdirectories:

- **`orchestrator/`** -- The orchestrator's own agent data.
- **`shared/`** -- Data accessible to all agents.

| Directory | Purpose |
|-----------|---------|
| `agent-data/orchestrator/agent/personas/` | System-created persona definitions |
| `agent-data/orchestrator/agent/context/` | System-created persistent context entries |
| `agent-data/orchestrator/agent/workflows/` | System-created workflow definitions |
| `agent-data/orchestrator/agent/entities/` | System-created entity memory |
| `agent-data/orchestrator/agent/profiles/` | System-created user profiles |
| `agent-data/orchestrator/agent/guardrails/` | System-created guardrail rules |
| `agent-data/orchestrator/user/*/` | Hand-written markdown files for any domain (read-only to system) |
| `agent-data/shared/agent/*/` | Shared data accessible to all agents |
| `agent-data/skills/` | Locally authored skill definitions |
| `skills.db` | SQLite database for skill records |
| `agent-data/orchestrator/agent/goals/` | System-created goal definitions for autonomous execution |
| `workqueue.db` | SQLite database for the autonomous execution work queue |
| `agentmailbox.db` | SQLite database for inter-agent message queue |
| `hangfire.db` | SQLite database for scheduled task state |
| `chathistory.db` | SQLite database for conversation history persistence |
| `vectorstore.db` | SQLite database for RAG document embeddings (only when `EmbeddingDeploymentName` is set) |

These directories are created automatically on first use. No manual setup is required.

---

## Chat Interface

AI Agent Canvas uses [CopilotKit](https://copilotkit.ai/) to provide a real-time chat interface where you interact with the agent. This page explains how the chat UI works and what to expect.

### Overview

The chat interface is a single-page application built with Next.js and the CopilotKit React components. It connects to the backend via the AG-UI protocol (Server-Sent Events) at the `/api/copilotkit` endpoint.

When you open **http://localhost:3000** in your browser, you see a full-screen chat panel ready for input.

### Sending Messages

Type your message in the input field at the bottom of the chat panel and press **Enter** to send. The agent receives your message, processes it, and streams back a response.

You can send:

- **Questions** -- "What is the current price of AAPL?"
- **Commands** -- "Create a persona called Analyst."
- **Multi-step requests** -- "Get stock quotes for AAPL, MSFT, and GOOGL, then summarize the trends."

### Streaming Responses

Responses stream in token by token as the LLM generates them. You see text appearing in real time rather than waiting for the full response to complete. This gives immediate feedback that the agent is working.

Long responses may take several seconds to complete. The streaming indicator shows that generation is still in progress.

### Tool Calls

When the agent needs to perform an action -- such as fetching a stock quote, creating a persona, or scheduling a task -- it makes a **tool call**. Tool calls appear in the chat as the agent transparently shows what action it is taking.

A typical tool call flow:

1. You send a message: "Get a stock quote for MSFT."
2. The agent decides to call the `stock_quote` tool.
3. The tool executes and returns data.
4. The agent incorporates the result into its response.

The agent has access to 70+ built-in tools spanning market data, personas, context, workflows, entities, user profiles, guardrails, goals, skills, scheduling, and MCP connections. See [Skills & Tools](#skills--tools) for details.

### Conversation Context

The agent maintains context throughout a conversation. It remembers what you discussed earlier in the session and can reference previous messages, tool results, and decisions.

Several systems contribute to the agent's context:

| Context Source | What It Provides |
|---------------|-----------------|
| Conversation history | All messages and tool results from the current session |
| Active persona | Custom instructions from the currently selected persona |
| Active user profile | Your name, role, timezone, and preferences |
| Persistent context | Saved context entries that carry across sessions |
| Entity memory | Known entities (people, companies, projects) |
| Guardrail rules | Active safety rules that shape behavior |

These context sources are injected into the agent's system prompt automatically. You do not need to repeat information that is already stored.

### Notifications

The backend can push real-time notifications to the frontend via Server-Sent Events (SSE) at `/api/notifications`. Scheduled tasks and background processes use this channel to report results without interrupting your current conversation.

### Tips for Effective Conversations

- **Be specific** -- "Get the stock quote for AAPL" works better than "tell me about Apple."
- **Chain requests** -- The agent can handle multi-step instructions in a single message.
- **Reference previous context** -- "Now do the same for MSFT" works when the agent has context from an earlier request.
- **Use natural language for management tasks** -- "List my personas," "Show scheduled tasks," and "What workflows do I have?" all work as expected.
- **Save important information** -- Ask the agent to save entities or context entries for facts you want it to remember across sessions.

### Troubleshooting

| Problem | Solution |
|---------|----------|
| Chat loads but messages fail | Check that the backend is running on port 5000. Verify `NEXT_PUBLIC_BACKEND_URL` in `frontend/.env.local`. |
| No response appears | Check the backend terminal for errors. Verify your `AIFoundry` settings in `appsettings.json`. |
| Responses are slow | Large models take longer. Consider using a faster deployment (e.g., `gpt-4o-mini`). Check your rate limit settings. |
| Tool calls fail | Check the backend logs for the specific tool error. Ensure network connectivity to Yahoo Finance and SEC EDGAR. |
| Connection refused | Make sure both the backend (`dotnet run`) and frontend (`npm run dev`) are running. |

### Architecture Note

The frontend is intentionally thin. It renders the CopilotKit chat component and points it at the backend's AG-UI endpoint. All agent logic, tool execution, and context management happen server-side. This means you can swap or customize the frontend without changing any agent behavior.

---

## Personas

Personas let you switch the agent's personality, tone, and focus area without changing any code. Each persona is a set of custom instructions that shape how the agent responds.

### What Is a Persona?

A persona is a named configuration with:

- **Name** -- A short identifier (e.g., "Analyst", "Advisor").
- **Description** -- What the persona is for.
- **Instructions** -- Detailed instructions injected into the agent's system prompt.

When a persona is active, its instructions are prepended to every agent response, guiding the agent's behavior, tone, and areas of focus. When no persona is active, the agent uses its default system prompt.

### Creating a Persona

Ask the agent to create one in natural language:

```
Create a persona called "Financial Analyst" with instructions to focus on
quantitative analysis, cite data sources, use precise financial terminology,
and present findings in a structured format with bullet points.
```

The agent calls the `create_persona` tool and saves the persona as a markdown file in `agent-data/personas/`.

### Listing Personas

To see all available personas:

```
List my personas
```

The agent calls `list_personas` and returns the name and description of each persona.

### Switching Personas

To activate a different persona:

```
Switch to the Financial Analyst persona
```

The agent calls `switch_persona`, and all subsequent responses in the session use that persona's instructions. The active persona is tracked in an `.active` file, so it persists across backend restarts.

### Reading a Persona

To view the full details of a persona:

```
Show me the Financial Analyst persona
```

The agent calls `read_persona` and displays the persona's name, description, and instructions.

### Updating a Persona

To change a persona's instructions:

```
Update the Financial Analyst persona to also include risk assessment in every analysis
```

The agent calls `update_persona` and saves the changes.

### Deleting a Persona

To remove a persona:

```
Delete the Financial Analyst persona
```

The agent calls `delete_persona` and removes the markdown file.

### How Personas Work Internally

Personas are managed by three components:

1. **PersonaStore** -- Reads and writes persona markdown files in `agent-data/personas/`. Each persona is a separate `.md` file.
2. **PersonaToolProvider** -- Exposes the six persona tools (`create_persona`, `update_persona`, `list_personas`, `switch_persona`, `read_persona`, `delete_persona`) to the agent.
3. **PersonaContextProvider** -- An AI context provider that reads the active persona's instructions and injects them into the agent's system prompt before every interaction.

### Example Personas

#### Data Analyst

```
Create a persona called "Data Analyst" with instructions:
- Focus on quantitative analysis and data interpretation
- Always ask for data sources and sample sizes
- Present findings with tables and bullet points
- Flag statistical anomalies and outliers
```

#### Executive Advisor

```
Create a persona called "Executive Advisor" with instructions:
- Provide concise, strategic recommendations
- Lead with the bottom line, then support with details
- Frame insights in terms of business impact and ROI
- Use professional but accessible language
```

#### Research Assistant

```
Create a persona called "Research Assistant" with instructions:
- Be thorough and cite sources whenever possible
- Present multiple perspectives on complex topics
- Organize findings with clear headings and sections
- Flag areas where information is uncertain or incomplete
```

### Tips

- **Only one persona is active at a time.** Switching personas replaces the previous one.
- **Persona instructions stack with other context.** Guardrails, user profiles, entities, and persistent context are still active alongside the persona.
- **Clear the persona** by asking "Switch to the default persona" or "Deactivate the current persona."
- **Keep instructions focused.** Shorter, clearer instructions produce more consistent behavior than long, complex ones.

---

## Skills & Tools

AI Agent Canvas comes with built-in tools and lets you create custom skills and connect external data sources via MCP. This page covers all three.

### Built-In Tools

The following tools are available out of the box when the MarketData MCP module is enabled:

| Tool | Description |
|------|-------------|
| `stock_quote` | Fetches the current stock price and key metrics for a given ticker symbol. |
| `edgar_company_facts` | Retrieves SEC EDGAR filing data for a company. |
| `stock_history` | Retrieves historical stock data from Yahoo Finance with a configurable range parameter. |

Use them by asking the agent in natural language:

```
Get a stock quote for AAPL
Show me the EDGAR company facts for Microsoft
Show me the stock history for TSLA over the last 6 months
```

The agent selects the appropriate tool, calls it, and incorporates the results into its response.

### What Are Skills?

Skills are user-defined tool definitions that extend the agent's capabilities. Unlike built-in tools, which are compiled into the application, skills are created at runtime through conversation.

Each skill has:

- **Name** -- A short identifier.
- **Description** -- What the skill does.
- **Prompt template** -- Instructions the agent follows when the skill is invoked.

When you run a skill, the agent executes the prompt template through the LLM, producing a result based on the skill's instructions.

### Creating Skills

Ask the agent to create a skill:

```
Create a skill called "Market Summary" that analyzes the current prices of
AAPL, MSFT, GOOGL, and AMZN and produces a one-paragraph market summary.
```

The agent calls `create_skill` and stores the skill in a SQLite database (`skills.db`).

### Listing and Running Skills

List your skills:

```
List my skills
```

Run a skill by name:

```
Run the Market Summary skill
```

The agent calls `run_skill`, which executes the skill's prompt template through the LLM and returns the result.

### Removing Skills

```
Remove the Market Summary skill
```

### Skill Authoring (Markdown-Based)

For more structured skill definitions, use the skill authoring system. Authored skills are saved as markdown files with YAML frontmatter in `agent-data/skills/`:

```
Author a skill called "Competitive Analysis" with description "Analyze
competitors in a given market segment" and tags "analysis, research"
```

The agent calls `author_skill` and creates a `.md` file. You can also edit and read authored skills:

```
Edit the Competitive Analysis skill to include pricing comparison
Show me the Competitive Analysis skill
Delete the Competitive Analysis skill
```

### Skill Registry (Catalog)

The skill registry provides a searchable catalog of pre-built skill templates. Browse and install skills from the catalog:

```
Search available skills for "analysis"
Show me the skill catalog
Install the "financial-report" skill
```

Installed skills with MCP endpoints return connection instructions so you can wire them up.

### Connecting External MCP Servers

The Model Context Protocol (MCP) lets you connect external data sources to the agent at runtime. Each MCP server exposes its own set of tools that become available to the agent.

#### Connecting a Server

```
Connect to MCP server "my-data-server" at https://mcp.example.com/sse using SSE transport
```

The agent calls `connect_mcp_server`, establishes a connection, discovers the server's tools, and registers them in the dynamic tool registry. The tools become available immediately with the `mcp:` prefix.

Supported transports:

- **`sse`** -- Server-Sent Events over HTTP (most common).
- **`http`** -- Standard HTTP transport.

#### Listing Connections

```
List my MCP connections
```

Shows all active MCP server connections and the tools each provides.

#### Disconnecting a Server

```
Disconnect the my-data-server MCP connection
```

Removes the connection and unregisters its tools.

### How Tools Are Registered

AI Agent Canvas uses a dynamic tool registry that aggregates tools from multiple sources:

1. **Built-in tools** -- Compiled into the application (MarketData tools).
2. **Agent data tools** -- Persona, context, workflow, entity, profile, guardrail, and goal management tools.
3. **Scheduling and autonomous tools** -- Scheduled tasks, start/stop autonomous mode, manage the work queue.
4. **Inter-agent tools** -- Agent registry, mailbox, and handoff tools for multi-agent collaboration.
5. **Dynamic MCP tools** -- Registered at runtime when you connect an MCP server.

All tools are available to the agent simultaneously. The agent selects the appropriate tool based on your request.

### Inter-Agent Communication Tools

AI Agent Canvas supports multi-agent collaboration. Each persona becomes a named agent that can be messaged, delegated to, or handed off to.

| Tool | Description |
|------|-------------|
| `list_available_agents` | List all agents available for handoff or messaging (built from personas). |
| `get_agent_info` | Get details about a specific agent. |
| `handoff_to_agent` | Delegate a task to another agent synchronously and get the result back. |
| `send_to_agent` | Send an asynchronous message to another agent's mailbox. |
| `check_inbox` | Check for pending messages from other agents. |
| `reply_to_message` | Reply to a message from another agent. |

Example usage:

```
List available agents
Hand off to the financial-analyst agent: "Analyze AAPL earnings for Q4"
Send a message to the code-reviewer agent: "Please review the latest changes"
```

### Tips

- **Skills are LLM-powered** -- They run prompt templates, not code. This makes them easy to create but means they depend on the LLM's capabilities.
- **MCP connections persist during the session** but are not saved across backend restarts. Reconnect MCP servers after restarting.
- **Tool names must be unique.** If an MCP server provides a tool with the same name as a built-in tool, the MCP version is registered under the `mcp:server-name` namespace.

---

## Scheduling

AI Agent Canvas includes a task scheduler powered by [Hangfire](https://www.hangfire.io/) that runs agent tasks in the background on a schedule or after a delay.

### What Scheduled Tasks Do

A scheduled task runs an agent prompt at a specified time or on a recurring schedule. The agent executes the prompt as if you had typed it in the chat, including access to all tools, personas, and context. Results are stored and can be retrieved later.

Use cases include:

- Periodic market data summaries
- Recurring report generation
- Timed alerts and checks
- Batch data processing

### Creating a Recurring Task

Ask the agent to create a recurring task using natural language:

```
Schedule a recurring task every hour to get stock quotes for AAPL and MSFT
and summarize the price movement
```

The agent calls `schedule_recurring_task` with a cron expression derived from your request. Common schedules:

| Request | Cron Expression |
|---------|----------------|
| Every hour | `0 * * * *` |
| Every day at 9 AM | `0 9 * * *` |
| Every Monday at 8 AM | `0 8 * * 1` |
| Every 30 minutes | `*/30 * * * *` |
| Every weekday at 6 PM | `0 18 * * 1-5` |

You can also specify a cron expression directly:

```
Schedule a recurring task with cron "0 9 * * 1-5" to generate a morning
market briefing
```

### Creating a One-Time Task

For tasks that should run once after a delay:

```
Schedule a one-time task in 30 minutes to check the current price of GOOGL
```

The agent calls `schedule_one_time_task` with the specified delay. The task runs once and stores its result.

### Viewing Scheduled Tasks

List all scheduled tasks:

```
List my scheduled tasks
```

The agent calls `list_scheduled_tasks` and returns the name, schedule, and status of each task.

### Getting Task Results

After a task has run, retrieve its results:

```
Get the results of my scheduled tasks
```

The agent calls `get_task_results` and returns the output from completed task executions.

### Removing a Scheduled Task

```
Remove the market briefing scheduled task
```

The agent calls `remove_scheduled_task` to cancel and delete the task.

### How It Works

The scheduling system uses three components:

1. **SchedulerToolProvider** -- Exposes the five scheduling tools to the agent: `schedule_recurring_task`, `schedule_one_time_task`, `list_scheduled_tasks`, `remove_scheduled_task`, and `get_task_results`.
2. **Hangfire** -- An open-source background job framework for .NET. It manages job queues, scheduling, retries, and persistence.
3. **SQLite storage** -- Task state is persisted in `hangfire.db`, so scheduled tasks survive backend restarts.

When a scheduled task fires, Hangfire invokes the agent with the stored prompt. The agent processes it using the same pipeline as a chat message, including tool calls and context injection.

### The Hangfire Dashboard

Hangfire provides a built-in web dashboard for monitoring and managing background jobs. Access it at:

```
http://localhost:5000/hangfire
```

The dashboard shows:

- **Recurring jobs** -- All recurring tasks with their cron expressions and next run times.
- **Succeeded jobs** -- Completed task executions with timestamps.
- **Failed jobs** -- Tasks that encountered errors, with exception details.
- **Processing jobs** -- Currently running tasks.
- **Scheduled jobs** -- One-time tasks waiting to execute.

Use the dashboard to inspect task history, trigger manual reruns, or delete jobs directly.

**Warning:** The Hangfire dashboard is exposed without authentication by default. In production, configure authentication middleware to restrict access. See the [Hangfire documentation](https://docs.hangfire.io/en/latest/configuration/using-dashboard.html#configuring-authorization) for details.

### Autonomous Execution Mode

Beyond scheduled tasks, AI Agent Canvas supports fully autonomous execution. In this mode, the agent works independently on goals without waiting for user input.

#### How It Works

1. **Create goals** -- Define what the agent should achieve using goal management tools or seed goals from custom agents.
2. **Enable autonomous mode** -- Ask the agent: *"Start autonomous mode"*. This registers a Hangfire recurring job.
3. **The agent works autonomously** -- The `AutonomousAgentJob` polls the work queue, claims items, executes them via `AIAgent.RunAsync`, and saves results. When the queue is empty, it picks the next active goal and creates work items for it.
4. **Monitor or stop** -- Ask *"Get autonomous status"* to check, or *"Stop autonomous mode"* to disable it.

#### Goals and Work Queue

Goals are markdown-persisted definitions that describe what the agent should accomplish. Each goal has a name, description, priority (critical/high/medium/low), acceptance criteria, and an optional assigned agent.

```
Create a goal called "Daily Market Report" with priority high and description
"Generate a comprehensive market analysis report for the top 10 tech stocks every morning"
```

The work queue is a SQLite-backed transient queue (`workqueue.db`) where individual work items are submitted, claimed, and completed. The autonomous agent job processes items in priority order.

```
Submit a work item: "Get stock quotes for AAPL, MSFT, and GOOGL and summarize trends"
List the work queue
Get queue stats
```

#### Goal and Work Queue Tools

| Tool | Description |
|------|-------------|
| `create_goal` | Create a new goal with priority and acceptance criteria |
| `list_goals` | List all goals, optionally filtered by status |
| `read_goal` | Read the full details of a goal |
| `update_goal_status` | Update a goal's status (active/completed/paused/cancelled) |
| `delete_goal` | Delete a goal |
| `submit_work_item` | Submit a work item to the queue |
| `list_work_queue` | List items in the work queue |
| `cancel_work_item` | Cancel a pending work item |
| `get_queue_stats` | Get work queue statistics |

#### Autonomous Mode Tools

| Tool | Description |
|------|-------------|
| `start_autonomous_mode` | Enable autonomous execution and register the Hangfire recurring job |
| `stop_autonomous_mode` | Disable autonomous execution and remove the recurring job |
| `get_autonomous_status` | Check whether autonomous mode is currently enabled |

#### Configuration

Autonomous execution is disabled by default. The options are:

| Option | Default | Description |
|--------|---------|-------------|
| `Enabled` | `false` | Whether autonomous mode is active |
| `MaxIterationsPerRun` | `5` | Maximum work items to process per job run |
| `PollIntervalSeconds` | `30` | Seconds between polls when queue is empty |
| `CronExpression` | `*/30 * * * * *` | Hangfire cron schedule for the recurring job |

### Tips

- **Tasks run server-side** -- They execute even when no browser is open.
- **Results are stored** -- You can retrieve results at any time after execution.
- **Tasks use the current agent configuration** -- The active persona, guardrails, and context at the time of execution apply to the task.
- **Monitor via notifications** -- Completed tasks can push results to the notification channel (`/api/notifications`), which the frontend receives via SSE.
- **SQLite persistence** -- The `hangfire.db` file stores all job state. Back it up if your scheduled tasks are important.

---

## Workflows

Workflows let you define multi-step agent sequences that run as a single operation. Instead of typing each step manually, you create a workflow once and run it whenever you need it.

### What Is a Workflow?

A workflow is a named sequence of steps stored as a markdown file. Each workflow has:

- **Name** -- A short identifier (e.g., "Morning Briefing").
- **Description** -- What the workflow accomplishes.
- **Tags** -- Optional labels for organization.
- **Content** -- The full workflow definition, including steps.

When you run a workflow, the agent reads its definition, builds a prompt from the steps, and executes it through the LLM. The agent has access to all its tools during execution, so workflow steps can fetch data, create entities, and perform any action the agent normally can.

### Creating a Workflow

Ask the agent to create a workflow in natural language:

```
Create a workflow called "Morning Briefing" with description "Daily market
overview" that does the following:
1. Get stock quotes for AAPL, MSFT, GOOGL, and AMZN
2. Get technical indicators for each stock
3. Summarize the overall market sentiment
4. List any stocks with significant price changes
```

The agent calls `create_workflow` and saves the workflow as a markdown file in `agent-data/workflows/`.

### Listing Workflows

To see all available workflows:

```
List my workflows
```

The agent calls `list_workflows` and returns the name, description, and tags of each workflow.

### Reading a Workflow

To view the full definition of a workflow:

```
Show me the Morning Briefing workflow
```

The agent calls `read_workflow` and displays the complete workflow content, including all steps.

### Running a Workflow

To execute a workflow:

```
Run the Morning Briefing workflow
```

The agent calls `run_workflow`, which:

1. Loads the workflow definition from its markdown file.
2. Builds a prompt from the workflow content.
3. Executes the prompt through the LLM with full tool access.
4. Returns the combined results.

The agent executes the steps sequentially, using tool calls as needed. You see the results stream in as the workflow progresses.

### Deleting a Workflow

```
Delete the Morning Briefing workflow
```

The agent calls `delete_workflow` and removes the markdown file.

### Workflow File Format

Workflows are stored as markdown files in `agent-data/workflows/`. Each file contains YAML frontmatter followed by the workflow content:

```yaml
---
name: Morning Briefing
description: Daily market overview
tags:
  - market
  - daily
---

## Steps

1. Get current stock quotes for AAPL, MSFT, GOOGL, and AMZN
2. Retrieve technical indicators for each stock
3. Summarize the overall market sentiment based on the data
4. Highlight any stocks with price changes greater than 2%
```

You can also edit these files directly if you prefer working with files over chat commands.

### Example Workflows

#### Portfolio Review

```
Create a workflow called "Portfolio Review" that:
1. Gets stock quotes for my portfolio: AAPL, MSFT, GOOGL, AMZN, TSLA
2. Gets EDGAR company facts for any stock that moved more than 3%
3. Produces a summary table with ticker, price, change, and recommendation
```

#### Competitor Analysis

```
Create a workflow called "Competitor Analysis" that:
1. Gets stock quotes for CRM, NOW, WDAY, and HUBS
2. Gets technical indicators for each
3. Compares their relative strength
4. Produces a competitive landscape summary
```

#### End of Day Report

```
Create a workflow called "End of Day Report" that:
1. Gets stock quotes for the top 5 holdings
2. Summarizes the day's market movements
3. Lists any significant news or filings from EDGAR
4. Recommends actions for the next trading day
```

### Combining Workflows with Scheduling

Workflows become more powerful when combined with scheduled tasks. For example:

```
Schedule a recurring task every weekday at 9 AM to run the Morning Briefing workflow
```

This creates a Hangfire job that executes your workflow automatically. See [Scheduling](#scheduling) for details.

### Tips

- **Workflows are LLM-driven** -- Steps are instructions, not code. The LLM interprets and executes them using available tools.
- **Tool access is automatic** -- The agent can call any registered tool during workflow execution, including MCP connections.
- **Results depend on available data** -- If a tool call fails (e.g., API rate limit), the workflow continues with what it has and notes the failure.
- **Keep steps clear and specific** -- Vague steps produce inconsistent results. "Get the stock quote for AAPL" works better than "check the market."
- **Edit files directly** -- Workflow markdown files in `agent-data/workflows/` can be edited with any text editor for fine-tuning.

---

## Security

AI Agent Canvas includes a layered security system covering governance policies, rate limiting, prompt injection detection, tool-call rules, security headers, and audit logging.

### Governance Policy Overview

The governance policy defines rules that control what the agent can and cannot do. Rules are defined in a YAML file (default: `governance-policy.yaml` in the web project root) and enforced by the `GovernanceKernel` at runtime.

The default policy includes these rules:

| Rule | Action | Description |
|------|--------|-------------|
| `block-dangerous-tools` | Deny | Blocks execution of the `run_script` tool. |
| `restrict-file-write-paths` | Deny | Prevents `write_file` from targeting system directories (`/etc`, `/var`, `C:\Windows`, `C:\Program Files`). |
| `block-private-mcp-endpoints` | Deny | Blocks `connect_mcp_server` from connecting to private or internal network addresses (SSRF protection). |
| `limit-scheduled-tasks` | Deny | Blocks scheduling tools when the rate limit is exceeded. |
| `allow-all-other` | Allow | Permits all tool calls not matched by a deny rule. |

#### Policy File Format

```yaml
name: AiAgentCanvas-default
rules:
  - name: block-dangerous-tools
    action: deny
    tools:
      - run_script
    description: Block script execution

  - name: restrict-file-write-paths
    action: deny
    tools:
      - write_file
    paths:
      - /etc
      - /var
      - C:\Windows
      - C:\Program Files
    description: Restrict file writes to safe locations

  - name: allow-all-other
    action: allow
    description: Permit everything else
```

#### Customizing the Policy

Edit `governance-policy.yaml` to add, modify, or remove rules. The policy path is configured in `appsettings.json`:

```json
{
  "Security": {
    "PolicyPath": "governance-policy.yaml"
  }
}
```

The conflict resolution strategy is **deny overrides** -- if any rule denies a tool call, it is blocked regardless of other allow rules.

### Rate Limiting

The backend enforces a fixed-window rate limit on all API requests. When a client exceeds the limit, the server responds with **HTTP 429 Too Many Requests**.

Configure the limit in `appsettings.json`:

```json
{
  "Security": {
    "RateLimitPerMinute": 30
  }
}
```

The default is 30 requests per minute. Increase this for high-throughput environments or decrease it to conserve API quota.

### Prompt Injection Detection

The `GovernanceKernel` includes built-in prompt injection detection. When enabled, it scans incoming messages for patterns that attempt to override the agent's instructions or extract system prompts.

This feature is enabled by default (`EnablePromptInjectionDetection: true`). Suspicious messages are flagged in the audit log and may be blocked depending on the governance configuration.

### Tool-Call Governance

Every tool call passes through the governance pipeline before execution. The pipeline evaluates the call against all active policy rules:

1. The agent decides to call a tool (e.g., `connect_mcp_server`).
2. The governance kernel evaluates the call against deny rules.
3. If any deny rule matches, the call is blocked and the agent receives an error.
4. If no deny rule matches, the call proceeds.

#### Governed MCP Gateway

MCP connections receive additional scrutiny through the `GovernedMcpGateway`:

- **Suspicious payload blocking** -- Payloads that contain potential injection patterns are blocked (`BlockOnSuspiciousPayload: true`).
- **Approval-required tools** -- Certain tools (`run_script`, `write_file`) require explicit approval before execution.

### Guardrails (User-Defined Safety Rules)

In addition to the system-level governance policy, you can create user-defined guardrail rules through conversation. Guardrails are injected into the agent's system prompt and guide its behavior.

#### Creating a Guardrail

```
Create a guardrail called "No Investment Advice" with severity "critical"
and rule "Never provide specific investment recommendations or buy/sell
signals. Always include a disclaimer that this is not financial advice."
```

#### Managing Guardrails

```
List my guardrails
Toggle the "No Investment Advice" guardrail off
Update the "No Investment Advice" guardrail to also prohibit price predictions
Delete the "No Investment Advice" guardrail
```

Guardrail severities:

| Severity | Purpose |
|----------|---------|
| `critical` | Rules the agent must never violate. |
| `warning` | Rules the agent should follow but may deviate from with justification. |
| `info` | Guidance that shapes behavior but does not restrict it. |

Guardrails are stored as markdown files in `agent-data/guardrails/` and injected into the system prompt by the `GuardrailContextProvider`.

### Security Headers

The backend adds the following security headers to all HTTP responses:

| Header | Value | Purpose |
|--------|-------|---------|
| `X-Content-Type-Options` | `nosniff` | Prevents MIME-type sniffing. |
| `X-Frame-Options` | `DENY` | Prevents the page from being embedded in frames (clickjacking protection). |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Controls referrer information sent with requests. |

These headers are applied automatically and require no configuration.

### Audit Logging

The governance system logs all tool calls and policy evaluations. Audit logging is enabled by default (`EnableAudit: true`) and records:

- Every tool call made by the agent
- The governance rules evaluated
- Whether the call was allowed or denied
- Timestamps and request details

Audit logs appear in the standard .NET logging output. Configure the log level in `appsettings.json` to control verbosity:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

Set to `Debug` for detailed governance evaluation traces.

### Security Checklist for Production

- Replace the `AIFoundry.Key` with a Managed Identity (`UseAzureCredential: true`).
- Verify Yahoo Finance and SEC EDGAR connectivity from the production network.
- Review and customize `governance-policy.yaml` for your use case.
- Add authentication to the Hangfire dashboard (`/hangfire`).
- Adjust `RateLimitPerMinute` for your expected load.
- Set `AllowedHosts` to your specific domain instead of `"*"`.
- Enable HTTPS and configure appropriate TLS settings.
- Review audit logs regularly for unusual tool call patterns.

---

> **[Download the complete PDF guide](guides/AI-Agent-Canvas-Guide.pdf)** | **[AI-First Company Guide](guides/AI-First-Company-Guide.pdf)**
