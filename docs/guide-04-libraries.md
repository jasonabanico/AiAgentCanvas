# 4. Libraries

AI Agent Canvas is built on a stack of libraries that each handle a specific concern. Understanding what each one does and where it fits will help you navigate the codebase and make informed decisions about what to extend.

## Microsoft Agent Framework (MAF)

The core agent runtime. MAF provides `HarnessAgent`, the base class for all agents, along with the middleware pipeline that processes each turn -- context injection, tool resolution, governance checks, and response handling. It defines how agents are registered, how they receive messages, and how multi-agent coordination works (handoff, background delegation, messaging).

Key packages:
- `Microsoft.Agents.AI` -- agent abstractions and the AI processing pipeline
- `Microsoft.Agents.Harness` -- `HarnessAgent`, middleware, and the agent execution loop
- `Microsoft.Agents.Workflows` -- workflow orchestration (sequential, concurrent)
- `Microsoft.Agents.Workflows.Declarative` -- YAML-based workflow definitions

## Microsoft.Extensions.AI (MEAI)

The abstraction layer between your agent code and the LLM provider. MEAI defines `IChatClient` for chat completions, `AIFunction` and `AITool` for tool definitions, and `IEmbeddingGenerator` for embeddings. The `FunctionInvokingChatClient` wraps any `IChatClient` and automatically handles the tool-call loop -- intercepting tool-call responses, executing functions, and feeding results back to the model. `DelegatingChatClient` enables middleware patterns like logging, caching, and governance wrapping.

MEAI makes the LLM backend swappable. The platform defaults to Azure AI Foundry, but any provider that implements `IChatClient` works without changing agent code.

## Azure AI Foundry (Azure OpenAI)

The default LLM provider. `AzureAIFoundryClientFactory` creates `IChatClient` and `IEmbeddingGenerator` instances configured for Azure OpenAI endpoints. It supports both API key and managed identity authentication, selected through configuration. The factory is registered in DI and consumed by the agent runtime -- agents never talk to Azure directly.

Azure AI Foundry is the default, not a requirement. Swapping to another provider means replacing the client factory registration. The rest of the platform is unaffected.

## AG-UI Protocol

The streaming protocol between the agent backend and the frontend. AG-UI uses Server-Sent Events (SSE) to deliver a typed event stream: text deltas, tool call starts and completions, reasoning content, state snapshot and delta updates, interrupt requests, and run lifecycle events (started, completed, error). The frontend consumes these events to render the agent's output progressively -- the user sees text appearing, tool calls executing, and state changing in real time.

The protocol is defined by a set of event types with structured payloads. The backend emits events as the agent processes each turn; the frontend subscribes and renders.

## A2A Protocol (Agent-to-Agent)

Cross-host agent communication over HTTP/JSON. A2A lets agents running in different processes or on different machines collaborate as if they were local. Each host publishes an `AgentCard` -- a JSON document describing the agent's name, capabilities, and endpoint. Remote agents are discovered by fetching their AgentCard and registered in the local agent registry.

Once registered, a remote agent is called through the same handoff and messaging interfaces as a local agent. The calling agent does not need to know whether its peer is in-process or across the network. A2A handles serialization, transport, and error propagation.

## Model Context Protocol (MCP)

A standard protocol for connecting agents to external data sources and tools. MCP servers expose tools (functions the agent can call) and resources (data the agent can read) over a defined interface. The platform's `McpConnectionManager` handles runtime connection lifecycle -- starting, stopping, and reconnecting to MCP servers as configured.

Tools from MCP servers are merged into the agent's tool set and subject to the same governance rules as native tools. This means policy-based filtering, approval flows, and audit logging apply uniformly, regardless of whether a tool is defined in code or provided by an MCP server.

## Microsoft Agent Governance Toolkit

Security and compliance middleware for agent operations. The governance toolkit provides two main capabilities: prompt injection detection (scanning inputs for attempts to manipulate the agent's behavior) and policy-based tool filtering using a deny-overrides evaluation model (if any policy denies a tool call, the call is blocked, regardless of other policies that allow it).

Governance decisions generate audit events that record what was evaluated, what the outcome was, and why. These events feed into the platform's observability pipeline for compliance reporting and incident investigation.

## Microsoft Purview

Compliance middleware for content classification and data loss prevention (DLP). Purview integration adds a middleware layer that classifies content flowing through the agent -- both inputs and outputs -- against sensitivity labels and DLP policies. If content matches a restricted classification, the middleware can block, redact, or flag it before it reaches the user or an external system.

This is relevant for agents operating in regulated environments where data handling must conform to organizational or regulatory policy.

## SQLite + Microsoft.Extensions.VectorData

Local persistence for all agent state. SQLite stores chat history (messages and metadata), entity memory (named entities the agent tracks across sessions), and scheduled tasks (deferred and recurring work). The vector store, built on `Microsoft.Extensions.VectorData`, adds embedding-based retrieval for the RAG pipeline.

The RAG implementation uses a hybrid search strategy: cosine similarity over embeddings for semantic relevance, combined with FTS5 full-text search for keyword precision. Documents are chunked, embedded via `IEmbeddingGenerator`, and stored in SQLite. At query time, both search paths run and results are merged and ranked.

SQLite was chosen for simplicity and portability -- no external database dependency, single-file storage, and good-enough performance for the agent's access patterns.
