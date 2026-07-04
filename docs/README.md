# AI Agent Canvas Documentation

Build intelligent multi-agent enterprise copilots with .NET and CopilotKit. Orchestrate specialized AI agents that reason, plan, and act through a shared tool registry.

## Key Features

- **Multi-Agent Architecture** -- Compose specialized agents that collaborate through a shared tool registry. Each agent owns its reasoning domain while the platform handles orchestration, context injection, and streaming.
- **Model Context Protocol** -- Connect to any data source through standardized MCP integrations. Agents access external systems through tools without importing SDKs or making HTTP calls directly.
- **Enterprise Security** -- OWASP LLM Top 10 coverage via Microsoft Agent Governance Toolkit. Prompt injection detection, tool-call filtering, audit logging, and rate limiting built in.
- **Extensible by Design** -- Add custom agents, tools, and MCP connections without touching the platform. Pure separation between the engine and your business logic.

## Documentation

| Section | Hub Page | Sub-Pages |
|---------|----------|-----------|
| **AI Agents** | [ai-agents.md](ai-agents.md) | [Fundamentals](ai-agents-fundamentals.md), [Framework](ai-agents-framework.md), [Tools](ai-agents-tools.md), [MCP](ai-agents-mcp.md), [Context](ai-agents-context.md), [Protocol](ai-agents-protocol.md) |
| **Use Cases** | [use-cases.md](use-cases.md) | |
| **User Guide** | [user-guide.md](user-guide.md) | [Getting Started](user-guide-getting-started.md), [Configuration](user-guide-configuration.md), [Chat](user-guide-chat.md), [Personas](user-guide-personas.md), [Skills](user-guide-skills.md), [Scheduling](user-guide-scheduling.md), [Workflows](user-guide-workflows.md), [Security](user-guide-security.md) |
| **Developer Guide** | [developer-guide.md](developer-guide.md) | [Architecture](developer-guide-architecture.md), [Agent Data](developer-guide-agent-data.md), [Skills & MCP](developer-guide-skills-mcp.md), [RAG](developer-guide-rag.md), [Security](developer-guide-security.md), [Adding Agents](developer-guide-adding-agents.md), [Behavior Patterns](developer-guide-behavior-patterns.md) |

## Downloadable Guides

- [AI Agent Canvas Complete Guide (PDF)](guides/AI-Agent-Canvas-Guide.pdf)
- [AI-First Company Guide (PDF)](guides/AI-First-Company-Guide.pdf)

## Folder Structure

```
docs/
├── README.md                              # This file
├── ai-agents.md                           # AI Agents hub
├── ai-agents-fundamentals.md              # What are AI agents
├── ai-agents-framework.md                 # Microsoft Agent Framework
├── ai-agents-tools.md                     # Tools and skills
├── ai-agents-mcp.md                       # Model Context Protocol
├── ai-agents-context.md                   # Context providers
├── ai-agents-protocol.md                  # AG-UI protocol
├── use-cases.md                           # Use cases
├── user-guide.md                          # User Guide hub
├── user-guide-getting-started.md          # Getting started
├── user-guide-configuration.md            # Configuration reference
├── user-guide-chat.md                     # Chat interface
├── user-guide-personas.md                 # Personas
├── user-guide-skills.md                   # Skills & tools
├── user-guide-scheduling.md               # Scheduling
├── user-guide-workflows.md                # Workflows
├── user-guide-security.md                 # Security
├── developer-guide.md                     # Developer Guide hub
├── developer-guide-architecture.md        # Architecture & core platform
├── developer-guide-agent-data.md          # Agent data domains
├── developer-guide-skills-mcp.md          # Skills & MCP connections
├── developer-guide-rag.md                 # RAG pipeline
├── developer-guide-security.md            # Security internals
├── developer-guide-adding-agents.md       # Adding custom agents
├── developer-guide-behavior-patterns.md   # Agent behavior patterns
├── guides/
│   ├── AI-Agent-Canvas-Guide.pdf
│   └── AI-First-Company-Guide.pdf
```
