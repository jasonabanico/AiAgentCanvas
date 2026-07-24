# 6. Context Domains

Context domains are the agent's knowledge system. Each domain stores a specific type of information as markdown files with YAML frontmatter. The platform injects this data into the system prompt before every LLM request, so the agent always has access to its configuration, policies, and accumulated knowledge. The agent can also manage domain data at runtime through built-in tools.

## The Three-Class Pattern

Every domain follows the same structure:

1. **Store** -- reads and writes markdown files from layered directories. Handles CRUD operations and file parsing via the `MarkdownFile` utility.
2. **Tool provider** -- exposes the store's operations as `AITool` instances so the LLM can create, read, update, and delete domain data through conversation.
3. **Context provider** -- an `AIContextProvider` that reads domain data from the store and injects it into the system prompt before each LLM call.

This pattern means data flows in both directions: the platform injects domain data *into* the LLM's context, and the LLM can modify domain data *through* tool calls. A user can say "add a guardrail that prevents sharing internal pricing" and the agent will create a guardrail file that takes effect on the next request.

## The Six Domains

| Domain | What It Stores | System Prompt Injection |
|--------|---------------|------------------------|
| **Personas** | Agent identities with name, description, and behavioral instructions | Active persona's instructions appended directly to the system prompt |
| **Context** | Typed knowledge entries: fact, reference, decision, or feedback | All entries grouped by type under a `## Persistent Context` section |
| **Entities** | Structured records describing people, companies, projects, or other domain objects | Entity index injected under `## Known Entities` |
| **Guardrails** | Policy rules with severity levels (critical, warning, info) and an enabled flag | Active rules injected under `## Guardrails & Policies` |
| **Profiles** | User-specific settings: role, timezone, and freeform preferences | Active profile injected under `## Active User Profile` |
| **Workflows** | Multi-step process definitions with descriptions and tags | Not injected into the system prompt; executed on demand when the user or agent triggers them |

Each domain stores its data as markdown files with YAML frontmatter. For example, a guardrail file looks like:

```markdown
---
name: investment-disclaimer
severity: high
enabled: true
---

Never provide specific buy, sell, or hold recommendations.
Always include a disclaimer that the analysis is informational only
and not investment advice.
```

## Data Storage Structure

Agent data lives under a common `agent-data/` root. Each agent gets its own subdirectory, with a `shared/` directory for cross-agent data. Within each subdirectory, `agent/` holds system-written files (seeds and tool-created content) and `user/` holds hand-written files that the system reads but never overwrites.

```
agent-data/
├── orchestrator/
│   ├── agent/                  # System writes here (seeds + tool-created content)
│   │   ├── personas/
│   │   ├── context/
│   │   ├── workflows/
│   │   ├── entities/
│   │   ├── profiles/
│   │   └── guardrails/
│   └── user/                   # Hand-written files (read-only to system)
│       ├── personas/
│       ├── context/
│       ├── workflows/
│       ├── entities/
│       ├── profiles/
│       └── guardrails/
└── shared/                     # Accessible to all agents
    ├── agent/
    │   ├── personas/
    │   ├── context/
    │   └── ...
    └── user/
        ├── personas/
        ├── context/
        └── ...
```

The `agent/` directory is where seed data lands on first startup and where tool-created content is saved. The `user/` directory is a no-code alternative to implementing seed interfaces: drop a properly formatted markdown file into the right subdirectory and the platform picks it up automatically.

## Layered Reading

Each store reads from multiple directories and merges the results. When the platform calls `ListAll()` on a domain store, it collects files from:

1. The agent's own `agent/` directory (system-written)
2. The agent's own `user/` directory (hand-written)
3. The `shared/agent/` directory (shared system-written)
4. The `shared/user/` directory (shared hand-written)

All files are merged into a single list. If two files have the same name, the agent-level file takes precedence over the shared-level file. This layering lets you define shared defaults (company-wide guardrails, common entity schemas) that individual agents can override with their own versions.

The context provider then reads this merged list and formats it into the appropriate system prompt section. The LLM sees user-provided, system-created, and shared data together, without needing to know where each piece came from.

## How Domains Interact

Domains are independent but complementary. A typical agent configuration might include:

- A **persona** that defines the agent's role and references specific tools
- **Guardrails** that constrain the persona's behavior (e.g., "never share internal pricing")
- **Context** entries that provide background knowledge the persona needs
- **Entities** that define the domain objects the agent works with
- A **profile** that captures the user's preferences and role
- **Workflows** that define reusable multi-step processes the agent can execute

All of these are injected into the system prompt together, giving the LLM a complete picture of who it is, what it knows, what rules it must follow, and what structured processes it can run.
