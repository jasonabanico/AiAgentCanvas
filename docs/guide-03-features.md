# 3. Features

## Agent Capabilities

These features apply to every agent, whether running standalone or as part of a multi-agent system.

| Feature | Description |
|---|---|
| **Dynamic personas** | Each agent has a persona that defines its identity, expertise, tone, and behavioral rules. Personas are injected into the system prompt at runtime and can be swapped or layered without changing code. |
| **Persistent context** | Domain-specific context (facts, rules, reference material) is loaded into the agent's system prompt on every turn. Context is defined in markdown files and assembled by the context manager. |
| **Entity memory** | Agents remember key entities (people, projects, systems, accounts) across conversations. Entities are stored in SQLite and recalled when relevant, giving the agent long-term awareness of the domain it operates in. |
| **Guardrails** | Behavioral boundaries defined in markdown that constrain what the agent will and will not do. Guardrails are injected into the system prompt alongside the persona, enforcing policy at the reasoning level. |
| **User profiles** | The agent knows who it is talking to -- name, role, preferences, and permissions. User profiles are loaded at the start of each session and shape how the agent responds. |
| **Skills** | Named, multi-step procedures the agent can execute. Skills are registered as tools and contain structured instructions the agent follows. They turn complex workflows into repeatable, reliable operations. |
| **Workflows** | Orchestrated sequences of steps involving multiple tools, decisions, and checkpoints. Workflows can be defined in code or declared in YAML for sequential, concurrent, or conditional execution. |
| **Scheduling** | Agents can schedule tasks for future or recurring execution. Scheduled tasks are persisted in SQLite and executed by a background service, enabling agents to set reminders, run periodic checks, or defer work. |
| **MCP connections** | Agents connect to external data sources and tools through the Model Context Protocol. MCP servers are configured at runtime via `McpConnectionManager` and their tools appear alongside native agent tools. |
| **RAG pipeline** | Retrieval-augmented generation backed by a SQLite vector store. Documents are chunked, embedded, and stored locally. At query time, the agent retrieves relevant chunks using cosine similarity with FTS5 hybrid search. |
| **System tools** | Built-in tools available to every agent: web search, URL reading, file operations, and system utilities. These are registered automatically and subject to the same governance as custom tools. |
| **File workspace** | `FileAccessProvider` gives agents a sandboxed file system for reading, writing, and managing files. Agents can work with uploaded documents, generate outputs, and organize artifacts in their workspace. |
| **Tool governance** | Policy-based filtering applied before any tool executes. Governance rules can deny, allow, or require approval for specific tools or tool categories, using a deny-overrides evaluation model. |
| **Tool approval** | Interactive approval flow for sensitive tool calls. Users can approve once, approve for the session ("don't ask again"), or deny. Approval decisions are cached so repeated calls to the same tool don't interrupt the flow. |
| **OpenTelemetry** | Distributed tracing and metrics for agent operations. Every LLM call, tool invocation, and agent turn is instrumented, providing observability into what the agent did and how long it took. |
| **Real-time streaming** | Server-Sent Events via the AG-UI protocol deliver text tokens, tool calls, reasoning steps, and state updates to the frontend as they happen. The user sees the agent thinking and acting in real time. |

## Multi-Agent Features

These features enable coordination between multiple agents within a single host or across hosts.

| Feature | Description |
|---|---|
| **Agent registry** | A central registry of all agents in the system, with their personas, tools, and capabilities. Agents discover each other through the registry, which the orchestrator uses to route requests to the right specialist. |
| **Handoff** | The `handoff_to_agent` mechanism transfers an active conversation from one agent to another. The receiving agent gets the conversation context and continues seamlessly. Used when a request falls outside the current agent's expertise. |
| **Background agents** | Parallel delegation to agents that work independently in the background. The primary agent continues its conversation while background agents execute tasks concurrently and report results when done. |
| **Agent messaging** | Asynchronous mailbox-based communication between agents. Agents can send and receive messages without blocking, enabling coordination patterns that don't require real-time handoff. |
| **A2A protocol** | Agent-to-Agent communication over HTTP/JSON across host boundaries. Remote agents are discovered through AgentCards that describe their capabilities. Once connected, remote agents behave like local agents -- the calling agent doesn't need to know whether its peer is in-process or across the network. |
| **Workflow orchestration** | Multi-agent workflows that coordinate work across agents in sequential, concurrent, or declarative patterns. Workflows can be defined in code or in YAML, specifying which agents handle which steps, how data flows between them, and what happens on failure. |
