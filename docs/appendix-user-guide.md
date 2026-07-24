# Appendix A: User Guide

## Getting Started

### Prerequisites

- .NET 9 SDK
- Node.js 20+
- Azure AI Foundry account (or any Azure OpenAI endpoint)

### Setup Steps

1. Clone the repository.
2. Build the backend:
   ```bash
   dotnet build
   ```
3. Configure `src/Host/AiAgentCanvas.Host/appsettings.Development.json` with your Azure AI Foundry credentials:
   ```json
   {
     "AIFoundry": {
       "Endpoint": "https://YOUR-RESOURCE.openai.azure.com",
       "Key": "your-api-key",
       "DeploymentName": "gpt-4o",
       "UseAzureCredential": false
     }
   }
   ```
4. Run the backend:
   ```bash
   dotnet run --project src/Host/AiAgentCanvas.Host
   ```
5. In a separate terminal, start the frontend:
   ```bash
   cd frontend
   npm install
   npm run dev
   ```
6. Open `http://localhost:3000` in your browser.

### Five Things to Try First

1. **Ask a question** -- type any question in the chat box and watch the streaming response.
2. **Create a persona** -- say "create a persona called Research Assistant that specializes in summarizing articles."
3. **Run a skill** -- say "create a skill called Summarize that takes a URL and returns a summary," then "run skill Summarize."
4. **Check a stock quote** -- say "get me a stock quote for MSFT" to exercise the market data tools.
5. **Create a workflow** -- say "create a workflow called Morning Briefing that checks stocks then summarizes news."

---

## Configuration Reference

### appsettings.json Structure

The base configuration file lives at `src/Host/AiAgentCanvas.Host/appsettings.json`.

| Section | Key | Type | Description |
|---------|-----|------|-------------|
| `AIFoundry` | `Endpoint` | string | Azure OpenAI endpoint URL |
| `AIFoundry` | `Key` | string | API key (leave blank if using Azure credential) |
| `AIFoundry` | `DeploymentName` | string | Chat model deployment name (e.g., `gpt-4o`) |
| `AIFoundry` | `EmbeddingDeploymentName` | string | Embedding model deployment; enables RAG when set |
| `AIFoundry` | `UseAzureCredential` | bool | Use `DefaultAzureCredential` instead of API key |
| `Security` | `PolicyPath` | string | Path to the governance policy YAML file |
| `Security` | `RateLimitPerMinute` | int | Maximum tool calls per minute (default: 30) |
| `VectorStore` | `ConnectionString` | string | SQLite connection string for the RAG vector store |
| `ApplicationInsights` | `ConnectionString` | string | Azure Monitor connection string for telemetry |

### Environment-Specific Overrides

ASP.NET Core loads configuration in layers. Place environment-specific values in:

- `appsettings.Development.json` -- local development settings (API keys, local endpoints).
- `appsettings.Production.json` -- production settings (real keys, production endpoints).

The environment-specific file merges on top of the base `appsettings.json`.

### Environment Variable Overrides

Any configuration key can be overridden via environment variables using the double-underscore separator:

```
AIFoundry__Key=your-key-here
AIFoundry__Endpoint=https://prod.openai.azure.com
Security__RateLimitPerMinute=60
```

### Frontend Configuration

The frontend reads its backend URL from `frontend/.env.local`:

```
NEXT_PUBLIC_BACKEND_URL=http://localhost:5000
```

Change this to point at your deployed backend in production.

---

## Using the Chat Interface

### UI Elements

- **Streaming responses** -- text appears token by token as the model generates it.
- **Tool call indicators** -- a status bar shows which tools are running and when they complete.
- **State panel** -- displays structured data returned by tools (stock quotes, task lists, entity data).
- **Reasoning blocks** -- shows chain-of-thought when the model reasons through a problem, then fades after 5 seconds.
- **Interrupt approve/deny buttons** -- when the agent requests approval for a sensitive action, buttons appear to approve or deny.
- **Health status banner** -- displays connection and service health at the top of the page.
- **Notifications** -- server-sent events (SSE) push real-time updates for scheduled tasks, workflow completions, and errors.

### Troubleshooting

| Problem | Likely Cause | Solution |
|---------|-------------|----------|
| No response after sending a message | Backend is not running or frontend cannot reach it | Verify the backend is running on port 5000 and `NEXT_PUBLIC_BACKEND_URL` in `.env.local` is correct |
| Response starts then times out | Model deployment is overloaded or the API key has expired | Check the backend console for HTTP 429 or 401 errors; verify `AIFoundry:Key` and `AIFoundry:Endpoint` |
| Tool call shows "blocked by governance policy" | Governance policy denied the tool call | Review `governance-policy.yaml` for deny rules matching the tool; adjust the policy or use a different approach |
| State panel stays blank | The tool does not have a `ToolStateMapping` registered | Only tools with a registered `ToolStateMapping` emit state events; check if the tool is mapped in its service extensions |
| Health check failed banner appears | Backend health endpoint returned an error | Check backend logs for startup failures; verify database connectivity and API key validity |

---

## Managing Agent Data

All agent data is managed through natural language commands in the chat. Each domain supports create, read, update, and delete operations.

### Personas

- "Create a persona called **Technical Writer** that writes clear, concise documentation."
- "Switch to the **Technical Writer** persona."
- "List all personas."
- "Delete the **Technical Writer** persona."

### Context

- "Save a fact: our fiscal year starts in July."
- "List all context entries."
- "Delete the context entry about fiscal year."

### Entities

- "Save an entity for **Acme Corp** -- they are a partner company based in Seattle."
- "Search entities for **Acme**."
- "List all entities."
- "Delete the entity **Acme Corp**."

### Guardrails

- "Create a guardrail called **No PII** with severity high: never include personal identifiable information in responses."
- "Toggle the **No PII** guardrail off."
- "List all guardrails."

### User Profiles

- "Create a profile called **Developer** with role engineer, timezone US/Pacific."
- "Switch to the **Developer** profile."
- "List all profiles."

### Workflows

- "Create a workflow called **Daily Report** that summarizes open tasks then drafts a status email."
- "Run the **Daily Report** workflow."
- "List all workflows."
- "Delete the **Daily Report** workflow."

### Skills

- "Create a skill called **Translate** that translates text to a given language."
- "Run the **Translate** skill with target language French."
- "List all skills."

### Scheduling

- "Schedule a task to run the **Daily Report** workflow every weekday at 9am."
- "List all scheduled tasks."
- "Cancel the **Daily Report** scheduled task."
