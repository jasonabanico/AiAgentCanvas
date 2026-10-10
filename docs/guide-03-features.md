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
| **Scheduling** | Agents can schedule tasks for future or recurring execution. Scheduled tasks are persisted in SQLite and executed by a background service. A task runs an agent, or a deterministic job when it names one. |
| **MCP connections** | Agents connect to external data sources and tools through the Model Context Protocol. MCP servers are connected at runtime through `McpConnectionManager`, or at startup from a seed, and their tools appear alongside native agent tools. The `connect_mcp_server` tool is blocked by default. The MCP connector is the governed route for an account-backed server. |
| **RAG pipeline** | Retrieval-augmented generation backed by a SQLite vector store. Documents are chunked, embedded, and stored locally. At query time, the agent retrieves relevant chunks using cosine similarity with FTS5 hybrid search, then reranks them with the model. Needs an embedding model on the Azure AI Foundry or Databricks provider. |
| **System tools** | Built-in tools to read, write and list files and to run scripts. Both work inside an allowlist, and an empty allowlist denies everything. They are subject to the same governance as custom tools. |
| **File workspace** | `FileAccessProvider` gives agents a sandboxed file system for reading, writing, and managing files. Agents can work with uploaded documents, generate outputs, and organize artifacts in their workspace. |
| **Tool governance** | Policy-based filtering applied before a tool executes, using a deny-overrides evaluation model. Tools named in `Security:ApprovalRequiredTools` are blocked until an operator removes them from the list. The defaults are `system_write_file`, `system_run_script`, `connect_mcp_server` and `schedule_task`. |
| **Tool approval** | Interactive approval flow for sensitive tool calls. Users can approve once, approve for the session ("don't ask again"), or deny. Approval decisions are cached so repeated calls to the same tool don't interrupt the flow. |
| **Episodic memory** | Agents remember past goals, outcomes, and tool usage across sessions. Episodes are stored in SQLite with automatic relevance decay and injected into the system prompt as context, giving agents the ability to learn from experience. |
| **Audit log** | Every model invocation, tool call, result, and error is recorded in a SQLite-backed audit trail. Sensitive parameters are automatically redacted. Agents can query their own audit history and retrieve aggregate statistics. |
| **Event triggers** | Proactive agent engagement through scheduled (cron), file-watch, webhook and connector triggers. Events wait in a durable SQLite queue with deduplication keys, retry with backoff, and a dead-letter state, so a restart loses none. |
| **Computer use** | Browser automation via headless Chromium (Playwright). Agents can navigate pages, click elements, type text, take screenshots, and extract page content, enabling web-based research and UI testing tasks. |
| **Evaluation** | Cases live in source, not in a database. `tests/AiAgentCanvas.EvalTests` holds checked-in cases graded by `Microsoft.Extensions.AI.Evaluation.Quality`, and CI runs them so a regression fails the build. The suite skips itself when no model is configured. |
| **Context budget** | Counts each prompt with the model's own tokenizer, enforces a ceiling per component, and compacts history with a summarizer before the provider truncates it without warning. |
| **Loop guard** | Gives each run an exit condition: a tool-round cap, a token budget, an optional cost cap, repeated-action detection and stagnation detection. When one trips it withdraws the tools and asks the model to report what it has. |
| **Cost tracking** | Records tokens and estimated cost for each model call, streaming included, using the rates in `Agent:Pricing`. |
| **Run ledger** | One SQLite record per scheduled, triggered, delegated, job, orchestration and external run, with usage, tool calls and outcome. Viewable in the Runs tab. |
| **Spend limits** | Daily limits per agent, per trigger and in total, measured from the run ledger. A trigger event over its limit is deferred. |
| **Jobs** | Deterministic work that runs without a model, started from a schedule, a trigger or an agent tool. |
| **Typed output** | Answers in a fixed JSON shape, checked against a schema and retried with the specific errors until they fit. |
| **Vision** | Tools that describe an image or extract typed data from one. Images come only from allowlisted folders and https hosts. |
| **Connections and connectors** | Encrypted credentials, OAuth with token refresh, and connectors to outside services. A tool that sends or deletes needs approval. |
| **Authentication** | Pluggable schemes (API key and JWT bearer) protect the endpoints. Off by default, with a startup warning while it is off. |
| **Cost-aware model routing** | A delegating chat client that scores request complexity and routes simple requests to an economy model while sending complex requests to the primary model. Reduces cost without sacrificing quality on hard tasks. |
| **Reflective reasoning** | A delegating chat client that injects reflection prompts after a configurable number of consecutive tool rounds, encouraging the agent to reassess its approach and correct course before continuing. |
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
| **Orchestration runs** | Group chat, handoff and Magentic runs with checkpoints on disk. A Magentic run stops for a person to approve its plan and survives a restart. Only a person can answer the review. |
| **MCP server** | Serves a chosen set of this host's tools to outside clients over MCP. Nothing is exposed until configuration names it. |
