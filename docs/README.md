# AI Agent Canvas Documentation

Build intelligent multi-agent enterprise copilots with .NET and CopilotKit. Orchestrate specialized AI agents that reason, plan, and act through a shared tool registry.

## Key Features

- **Multi-Agent Architecture** -- Compose specialized agents that collaborate through a shared tool registry. Each agent owns its reasoning domain while the framework handles orchestration, context injection, and streaming.
- **Model Context Protocol** -- Connect to any data source through standardized MCP integrations. Agents access external systems through tools without importing SDKs or making HTTP calls directly.
- **Enterprise Security** -- OWASP LLM Top 10 coverage via Microsoft Agent Governance Toolkit. Prompt injection detection, tool-call filtering, audit logging, and rate limiting built in.
- **Extensible by Design** -- Add custom agents, tools, and MCP connections without touching the framework. Pure separation between the engine and your business logic.

## Documentation

| Section | Markdown | Website |
|---------|----------|---------|
| **AI Agents** | [ai-agents.md](ai-agents.md) | [website/ai-agents/](website/ai-agents/what-are-ai-agents.html) |
| **Use Cases** | [use-cases.md](use-cases.md) | [website/use-cases/](website/use-cases/index.html) |
| **User Guide** | [user-guide.md](user-guide.md) | [website/user-guide/](website/user-guide/getting-started.html) |
| **Developer Guide** | [developer-guide.md](developer-guide.md) | [website/developer-guide/](website/developer-guide/architecture-overview.html) |

The **Markdown** files are readable directly in GitHub and VS Code. The **Website** files are the full HTML documentation with navigation, styling, and diagrams.

## Downloadable Guides

- [AI Agent Canvas Complete Guide (PDF)](guides/AI-Agent-Canvas-Guide.pdf)

## Folder Structure

```
docs/
├── README.md                   # This file
├── index.html                  # Website landing page
├── ai-agents.md                # AI Agents (markdown)
├── use-cases.md                # Use Cases (markdown)
├── user-guide.md               # User Guide (markdown)
├── developer-guide.md          # Developer Guide (markdown)
├── guides/
│   └── AI-Agent-Canvas-Guide.pdf
├── website/
│   ├── styles.css
│   ├── layout.js
│   ├── images/                 # Architecture diagrams (SVG)
│   ├── ai-agents/              # 6 HTML pages
│   ├── use-cases/              # 6 HTML pages
│   ├── user-guide/             # 8 HTML pages
│   └── developer-guide/        # 9 HTML pages
├── generate_pdf.py             # PDF generator script
└── generate_dsar_agent_pdf.py  # DSAR guide generator (internal)
```
