> [User Guide](user-guide.md) > Configuration

# User Guide: Configuration

All backend configuration lives in `src/Orchestrator/AiAgentCanvas.Orchestrator/appsettings.json`. This page covers every section and how to customize it for your environment.

## Configuration File Structure

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

## AIFoundry Section

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

### Azure AI Foundry Setup

1. Create an Azure AI Foundry resource in the Azure Portal.
2. Deploy a model (e.g., `gpt-4o`) and note the deployment name.
3. Copy the endpoint URL and API key from the resource's Keys and Endpoint page.
4. Paste them into the `AIFoundry` section.

### Using Azure Managed Identity

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

## Security Section

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
| `PolicyPath` | `governance-policy.yaml` | Path to the YAML file defining governance rules. See [Security](user-guide-security.md). |
| `RateLimitPerMinute` | `30` | Maximum API requests per minute per client. Returns HTTP 429 when exceeded. |

## VectorStore Section

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

## Logging Section

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

## Environment-Specific Configuration

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

## Environment Variables

Any setting can be overridden via environment variables using the `__` (double underscore) separator:

```
export AIFoundry__Key="your-key-here"
export Security__RateLimitPerMinute=60
```

This is useful for CI/CD pipelines and container deployments where you do not want secrets in config files.

## Frontend Configuration

The frontend reads its backend URL from `frontend/.env.local`:

```
NEXT_PUBLIC_BACKEND_URL=http://localhost:5000
```

Change this when the backend runs on a different host or port.

## Runtime Data Directories

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
