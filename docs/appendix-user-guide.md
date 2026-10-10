# Appendix A: User Guide

## Getting Started

### Prerequisites

- .NET 10 SDK
- Node.js 22 or later
- A model provider: an Azure OpenAI deployment, a Databricks serving endpoint, or a Snowflake Cortex account

### Setup Steps

1. Clone the repository.
2. Build the backend. A solution build also copies the sample agent and market data plugins into the Host's `plugins/` folder:
   ```bash
   dotnet build AiAgentCanvas.sln
   ```
3. Configure `src/Host/AiAgentCanvas.Host/appsettings.Development.json` with your provider credentials. This example uses Azure AI Foundry:
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
   To use Databricks or Snowflake, set `Provider` to `Databricks` or `Snowflake` and fill in that provider's section.
4. Turn on the capabilities you want. All flags default to `false`, so a fresh start is a bare chat agent. This set matches the examples below:
   ```json
   {
     "Features": {
       "Personas": true,
       "Context": true,
       "Guardrails": true,
       "Skills": true,
       "SkillAuthoring": true,
       "Workflows": true,
       "InterAgentCommunication": true,
       "RunLedger": true
     },
     "Agents": { "FinancialAnalyst": { "Enabled": true } },
     "DataConnections": { "MarketData": { "Enabled": true } }
   }
   ```
5. Run the backend. It listens on `http://localhost:5149`:
   ```bash
   dotnet run --project src/Host/AiAgentCanvas.Host
   ```
6. In a separate terminal, start the frontend. The dev server forwards `/api` requests to the backend:
   ```bash
   cd frontend
   npm install
   npm run dev
   ```
7. Open `http://localhost:3000` in your browser.

### Five Things to Try First

These assume the flags from step 4 above.

1. **Ask a question.** Type any question in the chat box and watch the streaming response.
2. **Create a persona.** Say "create a persona called Research Assistant that specializes in summarizing articles."
3. **Create and run a skill.** Say "create a skill called Summarize that takes a URL and returns a summary," then "run skill Summarize."
4. **Check a stock quote.** Say "get me a stock quote for MSFT" to exercise the market data tools.
5. **Create a workflow.** Say "create a workflow called Morning Briefing that checks stocks then summarizes news."

After a few unattended runs, open the **Runs** tab to see what each one did and what it cost.

---

## Configuration Reference

### appsettings.json Structure

The base configuration file lives at `src/Host/AiAgentCanvas.Host/appsettings.json`. The template for new deployments is `appsettings.template.json` in the same folder. The main sections and keys:

| Section | Key | Description |
|---------|-----|-------------|
| (root) | `Provider` | The LLM backend: `AzureAIFoundry` (default), `Databricks` or `Snowflake` |
| `AIFoundry` | `Endpoint`, `Key`, `DeploymentName` | Azure OpenAI endpoint, API key and chat deployment (for example `gpt-4o`) |
| `AIFoundry` | `UseAzureCredential` | Use `DefaultAzureCredential` instead of an API key |
| `AIFoundry` | `EmbeddingDeploymentName` | Embedding deployment. RAG needs it, together with the `Rag` flag |
| `AIFoundry` | `EconomyDeploymentName`, `JudgeDeploymentName` | A cheaper model for routing and summaries, and a separate model for the evaluation suite |
| `Databricks`, `Snowflake` | `WorkspaceUrl` or `AccountUrl`, token, `ModelName` | The same settings for those providers, with `EmbeddingModelName`, `EconomyModelName` and `JudgeModelName` |
| `Security` | `PolicyPath` | Path to the governance policy YAML file |
| `Security` | `ApprovalRequiredTools` | Tool names that governance blocks. Defaults to `system_write_file`, `system_run_script`, `connect_mcp_server`, `schedule_task` |
| `Security` | `RateLimitPerMinute` | Requests per minute for the `agent` rate-limit policy (default 30). See the note in the security reference. |
| `Authentication` | `Enabled`, `Schemes`, `AllowAnonymous`, `AllowedOrigins`, `ApiKey`, `JwtBearer` | Endpoint authentication. Off by default. |
| `Agent` | `ContextBudget`, `LoopGuard`, `Pricing`, `Reflection`, `ModelRouter` | The chat pipeline. See Platform Internals. |
| `Agent` | `Scheduler`, `EventTriggers`, `Budgets`, `RunLedger`, `Jobs` | Unattended work, run records and spend limits. See Operations and Connectors. |
| `Agent` | `Structured`, `Vision`, `Orchestration`, `McpServer` | The typed output, vision, orchestration and MCP server capabilities |
| `SystemTools` | `AllowedPaths`, `AllowedCommands`, `MaxFileSizeBytes`, `ScriptTimeoutSeconds` | Filesystem and shell allowlists. Empty means deny. |
| `Connections` | `PublicBaseUrl`, `KeyRingPath`, `OAuthApps` | The credential store and OAuth |
| `Connectors` | `ApprovalMode`, `CheckIntervalSeconds` | The connector host |
| `ChatHistory` | `ConnectionString`, `MaxMessages` | SQLite chat history |
| `VectorStore` | `ConnectionString` | SQLite connection string for the RAG vector store |
| `ApplicationInsights` | `ConnectionString` | Azure Monitor connection string. Also exports the platform's own spans and metrics. |

### Feature Flags

Every capability in the platform can be individually enabled via the `Features` section. All flags default to `false` -- the platform is opt-in, so a fresh deployment registers nothing beyond the LLM provider and security until you turn a capability on. Set a flag to `true` to register that capability's services, tools, and endpoints.

```json
{
  "Features": {
    "Personas": false,
    "Context": false,
    "Guardrails": false,
    "UserProfiles": false,
    "Entities": false,
    "Skills": false,
    "SkillRegistry": false,
    "SkillAuthoring": false,
    "Workflows": false,
    "Mcp": false,
    "SystemTools": false,
    "Notifications": false,
    "Scheduling": false,
    "Rag": false,
    "InterAgentCommunication": false,
    "EpisodicMemory": false,
    "AuditLog": false,
    "EventTriggers": false,
    "ComputerUse": false,
    "RunLedger": false,
    "Jobs": false,
    "Connections": false,
    "Connectors": false,
    "StructuredOutput": false,
    "Vision": false,
    "AgentOrchestration": false,
    "McpServer": false
  }
}
```

| Flag | Description |
|------|-------------|
| `Personas` | Dynamic persona management. Each agent gets an identity, expertise area, tone, and behavioral rules injected into its system prompt. Personas can be created, swapped, or layered at runtime. |
| `Context` | Persistent domain-specific context (facts, rules, reference material) loaded into the agent's system prompt on every turn. |
| `Guardrails` | Behavioral boundaries that constrain what the agent will and will not do. Guardrails are injected into the system prompt alongside the persona, enforcing policy at the reasoning level. |
| `UserProfiles` | User identity and preferences. The agent knows who it is talking to -- name, role, preferences, and permissions -- and adjusts its responses accordingly. |
| `Entities` | Long-term entity memory. Agents remember key entities (people, projects, systems, accounts) across conversations, stored in SQLite and recalled when relevant. |
| `Skills` | Named, multi-step procedures the agent can invoke by name. Skills are registered as tools and contain structured instructions the agent follows to execute complex workflows repeatably. Stored under `agent-data/skills`. |
| `SkillRegistry` | Discovery and listing of all registered skills. Enables agents to browse available skills and invoke them by name. |
| `SkillAuthoring` | Agents can create and edit skills at runtime through natural language instructions. New skills are persisted and immediately available. |
| `Workflows` | Orchestrated multi-step sequences involving tools, decisions, and checkpoints. Supports sequential, concurrent, and declarative (YAML) execution patterns. |
| `Mcp` | Model Context Protocol client connections. Registers the connection manager and its tools, and connects the servers named by `IMcpConnectionSeed` at startup. The `connect_mcp_server` tool is blocked by default. |
| `SystemTools` | Tools to read, write and list files and to run scripts, inside `SystemTools:AllowedPaths` and `AllowedCommands`. Governed by the same policy pipeline as custom tools. `system_write_file` and `system_run_script` are blocked by default. |
| `Notifications` | Agent-to-user notification delivery via SSE. Registers the notification store, tool provider, and the `/api/notifications` HTTP endpoints. |
| `Scheduling` | Cron-based and one-time scheduled tasks that run an agent or a job. Persisted in SQLite and executed by a background service. `schedule_task` is blocked by default. |
| `Rag` | Retrieval-augmented generation backed by a vector store. Documents are chunked, embedded, and stored. At query time, the agent retrieves relevant chunks with hybrid search and reranking. Needs an embedding model on the Azure AI Foundry or Databricks provider. |
| `InterAgentCommunication` | Multi-agent coordination: agent registry, in-process handoff, background delegation, and asynchronous mailbox-based messaging between agents. |
| `EpisodicMemory` | Agents remember past goals, outcomes, and tool usage across sessions. Episodes are stored in SQLite with automatic relevance decay (5% every 6 hours, pruned below 1%). Recent episodes are injected into the system prompt as context. |
| `AuditLog` | Every model invocation, tool call, result, and error is recorded in a SQLite-backed audit trail. Sensitive parameters (keys, tokens, passwords) are automatically redacted. Agents can query their own history and retrieve aggregate statistics. |
| `EventTriggers` | Proactive agent engagement through scheduled (cron), file-watch, webhook and connector triggers. Events wait in a durable SQLite queue with deduplication, retry and a dead-letter state. Registers the trigger service, tool provider, and the `/api/triggers` HTTP endpoints. |
| `ComputerUse` | Browser automation via headless Chromium (Playwright). Agents can navigate pages, click elements by coordinates or CSS selector, type text, take screenshots, and extract page content. |
| `RunLedger` | One SQLite record per scheduled, triggered, delegated and job run: input, output, tool calls, tokens, estimated cost and how it ended. Adds the `/api/runs` endpoints and the Runs tab. Daily spend limits (`Agent:Budgets`) need it. |
| `Jobs` | Deterministic jobs that run without a model, started from a schedule, a trigger or an agent tool. |
| `Connections` | Encrypted credential storage, the OAuth connect flow with PKCE, and token refresh. Adds the `/api/connections` endpoints and the Connections tab. |
| `Connectors` | Connectors to outside services (Twilio SMS, Gmail and other MCP servers). Needs `Connections`. Adds the connector host, `/api/connectors/{connectionId}/webhook`, and connector triggers. |
| `StructuredOutput` | Answers in a fixed JSON shape, checked against a schema and retried with the errors until they fit. Adds `extract_structured`, `list_schemas` and the `/api/structured` endpoints. |
| `Vision` | Tools that describe an image or extract typed data from it. Images come from allowlisted folders and https hosts. Needs a vision-capable model. |
| `AgentOrchestration` | Group chat, handoff and Magentic runs with checkpoints and a human sign-off step. Needs `InterAgentCommunication`. Adds the `/api/orchestrations` endpoints and the Orchestrations tab. |
| `McpServer` | Serves a chosen set of tools over the Model Context Protocol. Exposes nothing until `Agent:McpServer:ExposedTools` names it, and refuses to start with authentication off unless told otherwise. |

The run ledger, spend limits, durable triggers, jobs, connections and connectors are described in [Operations and Connectors](guide-11-operations-and-connectors.md). Typed output, vision, orchestration and the MCP server are described in [Typed Output, Vision, Orchestration and MCP Server](guide-12-typed-output-vision-orchestration-and-mcp-server.md).

### Service Modules (Agents and Data Connections)

Agent projects and data-connection projects use the `IServiceModule` interface instead of feature flags. Each module declares a configuration section name and self-registers its services. Host does not reference these projects at compile time at all: they're loaded at runtime from a `plugins/` folder next to the Host executable, one subfolder per plugin, via `System.Runtime.Loader.AssemblyLoadContext`. `ServiceModuleExtensions.AddServiceModules` scans that folder, loads each plugin into its own isolated `PluginLoadContext`, and reflects over its types for `IServiceModule` implementations.

Modules are opt-in, like feature flags: a module with no configuration section, or with `Enabled` absent, stays disabled. Set `Enabled` to `true` in its configuration section to register it:

```json
{
  "Agents": {
    "FinancialAnalyst": {
      "Enabled": true
    }
  },
  "DataConnections": {
    "MarketData": {
      "Enabled": true
    }
  }
}
```

To create a new module, implement `IServiceModule` in your agent or data-connection project:

```csharp
public sealed class MyAgentModule : IServiceModule
{
    public string SectionName => "Agents:MyAgent";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Register persona seeds, tools, context, etc.
    }
}
```

Then wire up the project so its build output lands where the host will look for it: add `<GenerateDependencyFile>true</GenerateDependencyFile>` to its `.csproj`, plus a post-build target that copies the output into `$(HostPluginsDir)$(MSBuildProjectName)\` (copy the target verbatim from an existing plugin project, e.g. `Agent.FinancialAnalyst.csproj`). No `ProjectReference` from Host, no `Program.cs` edit, no line in Host's project file at all. The project only needs to be part of the solution so a normal build produces its output; Host finds it by folder, not by name.

Under the hood, `PluginLoadContext` resolves each plugin's own private dependencies from its folder via `AssemblyDependencyResolver`, but defers to whatever the host has already loaded for shared assemblies, above all `AiAgentCanvas.Abstractions`. Skipping that fallback would give the plugin's copy of `IServiceModule` a different runtime identity than the host's, and the type check that finds modules would silently fail.

### Provider-Specific Configuration

Provider-specific settings are nested under their provider section. The Databricks Vector Search tool, for example, is configured under `Databricks:VectorSearch` and self-gates based on whether the required settings (workspace URL, token, index name) are present:

```json
{
  "Databricks": {
    "WorkspaceUrl": "https://your-workspace.cloud.databricks.com",
    "ModelName": "databricks-meta-llama-3-3-70b-instruct",
    "VectorSearch": {
      "WorkspaceUrl": "https://your-workspace.cloud.databricks.com",
      "PersonalAccessToken": "dapi...",
      "IndexName": "main.default.docs_index"
    }
  }
}
```

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

The frontend needs no backend URL setting. In development, `npm run dev` forwards requests to `/api` to `http://localhost:5149`, the address in the Host's launch settings. To point the dev server somewhere else, change the rewrite in `frontend/next.config.ts`.

A production build is a static export. The Host serves it from `wwwroot`, so the browser and the API share one origin. The Docker image listens on port 5000.

---

## Using the Chat Interface

### UI Elements

- **Tabs** -- Chat, Runs (the run ledger with totals and detail), Orchestrations (multi-agent runs, and where a person answers a plan review) and Connections (connected accounts).
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
| No response after sending a message | Backend is not running or the frontend cannot reach it | Verify the backend is running on `http://localhost:5149` and that the dev server's `/api` rewrite points to it |
| Response starts then times out | Model deployment is overloaded or the API key has expired | Check the backend console for HTTP 429 or 401 errors; verify `AIFoundry:Key` and `AIFoundry:Endpoint` |
| Tool call shows "blocked by governance policy" | Governance policy denied the tool call | Review `governance-policy.yaml` for deny rules matching the tool; adjust the policy or use a different approach |
| State panel stays blank | The tool does not have a `ToolStateMapping` registered | Only tools with a registered `ToolStateMapping` emit state events; check if the tool is mapped in its service extensions |
| Health check failed banner appears | Backend health endpoint returned an error | Check backend logs for startup failures; verify database connectivity and API key validity |
| A tool call is refused and the tool is on the default list | `Security:ApprovalRequiredTools` blocks `system_write_file`, `system_run_script`, `connect_mcp_server` and `schedule_task` | Remove the name from the list only if you accept what the tool can do |
| API calls return 401 | `Authentication:Enabled` is true and the request carries no credentials | Send the `X-API-Key` header, or a bearer token for the configured authority |
| The Host stops at startup with a message about `Features:McpServer` | The MCP server is on and authentication is off | Turn authentication on, or set `Agent:McpServer:AllowUnauthenticated` for a trusted network |
| A connection shows `NeedsReauth` | The provider refused the stored credential | Choose **Reconnect** on the Connections tab |
| An orchestration run is `WaitingForInput` | A Magentic plan needs a person's approval | Open the Orchestrations tab and approve, ask for changes, or cancel |

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

`schedule_task` is on the default approval-required list, so governance blocks it until you remove it from `Security:ApprovalRequiredTools`.
