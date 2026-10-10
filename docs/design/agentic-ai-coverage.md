# Coverage of "Agentic AI" Part V

This document checks AI Agent Canvas against Part V of *The Hitchhiker's Guide to Agentic AI* by H. Roitman: the "Agentic AI" part, pages 304 to 543 of the PDF, chapters 15 to 27. For each concept the chapters name, it says whether the platform covers it, covers part of it, or leaves it out, and where in the code the answer lives.

**Statuses.**

- **Covered**: the platform does it, with a test.
- **Partial**: the platform does a useful part, or the agent can compose it from other pieces. The note says what is missing.
- **Not covered**: nothing in the platform does it.
- **Out of scope**: the concept is about training models, running research benchmarks or comparing other frameworks. AI Agent Canvas is a runtime for agents, so it does not train models.

## Summary

| Chapter | Covered | Partial | Not covered | Out of scope |
|---|---|---|---|---|
| 15 Introduction | 5 | 0 | 0 | 0 |
| 16 RAG | 14 | 8 | 11 | 4 |
| 17 Memory | 13 | 9 | 7 | 4 |
| 18 Harness | 25 | 11 | 12 | 1 |
| 19 Loop engineering | 11 | 8 | 2 | 2 |
| 20 Design patterns | 10 | 3 | 1 | 0 |
| 21 Environments and benchmarks | 4 | 2 | 0 | 6 |
| 22 MCP | 11 | 2 | 4 | 2 |
| 23 Agent skills | 5 | 3 | 3 | 1 |
| 24 A2A | 6 | 7 | 6 | 0 |
| 25 Multi-agent systems | 9 | 10 | 4 | 2 |
| 26 Frameworks | 6 | 4 | 3 | 1 |
| 27 Agentic UI | 9 | 6 | 6 | 3 |
| **Total** | **128** | **73** | **59** | **26** |

The table counts the rows of the chapter tables below. A row can name several related techniques. Of 286 rows, 128 are covered, 73 are partial, 59 are not covered and 26 are out of scope. The platform covers the runtime concerns of the book well: context management, loop control, tool governance, multi-agent coordination and the user interface. It leaves out the research techniques that need model training and the retrieval and memory techniques that need a graph or a clustering step.

## What this review changed

The review compared the book with the code and produced these changes, each with tests.

| Change | Chapter | Why |
|---|---|---|
| Documents could not get into the index. `DocumentChunker` was registered and nothing called it. Ingestion, versioning, replacement, expiry, and the `rag_search` and `rag_list_documents` tools are new. | 16 | The retrieval half of RAG existed without the indexing half. |
| Hybrid search fuses the vector and keyword rankings with reciprocal rank fusion, and several phrasings of a question fuse the same way. | 16 | The book's recommended default. A cosine similarity and a keyword rank are not on one scale. |
| The keyword index is a table of its own. The earlier external-content form could not delete rows. Older databases are rebuilt on first open. | 16 | A deleted chunk would stay findable by keyword. |
| Near-duplicate episodes merge, recalled episodes are refreshed, and episodes can be listed and deleted by a person or by the agent on request. | 17 | The book names duplicate suppression, spaced repetition and the right to inspect and delete stored memory. |
| Sequential, Concurrent and Review orchestration kinds. | 19, 20, 25 | Prompt chaining, parallelization with voting inputs, and the maker-checker loop that chapter 19 calls the most important structural principle. |
| A cap on the size of one tool result. | 18 | One large result pushed the rest of the conversation out of the prompt. |
| Optional relevance-based tool selection. | 18 | The prompt budget dropped tools by position in the list. |
| Tools from a connected MCP server honour the server's `destructiveHint` and ask for approval. | 22 | MCP tool annotations exist so a host can gate destructive tools. |
| The `Local` provider, with a guard that refuses a public endpoint. | 18, 26 | There was no way to run without a hosted model. |
| The context providers return only what they add. Before, each one returned the incoming context, and the agent merged that into a copy of itself. | 18 | A host with a few features sent a 42,000 token system prompt, 672 tool definitions for 21 tools, and the user message several times. The budget then cut the tail, where retrieved documents and memories sit. |

The last row is a defect that predates this review. It showed up when the host was run against a model server and the server's log was read. The chat pipeline's duplicate-tool filter and the prompt budget had been hiding it.

## Chapter 15: Introduction

| Concept | Status | Where |
|---|---|---|
| Persistence across turns and sessions | Covered | Chat history, episodic memory, entities, context, profiles |
| Grounding in outside knowledge | Covered | RAG, vector search tools, market data |
| Action through defined interfaces | Covered | Tools, connectors, MCP |
| Coordination of several agents | Covered | Handoff, messaging, orchestration |
| Safety, oversight and graceful stops | Covered | Governance, approvals, loop guard, budgets |

## Chapter 16: Retrieval-augmented generation

| Concept | Status | Where or what is missing |
|---|---|---|
| Indexing pipeline: load, chunk, embed, store | Covered | `RagIngestionService`, `POST /api/rag/documents`. The caller supplies plain text. PDF and HTML loaders are not provided. |
| Fixed-size chunking with overlap, paragraph and sentence aware | Covered | `DocumentChunker`, sizes in `Agent:Rag` |
| Semantic chunking | Not covered | Needs a similarity pass over sentences |
| Structure-aware chunking for Markdown headers or code | Not covered | Paragraph boundaries only |
| Parent-child chunking | Not covered | |
| Dense retrieval | Covered | Cosine similarity over the stored vectors |
| Sparse retrieval (BM25) | Covered | SQLite FTS5 |
| Hybrid retrieval with reciprocal rank fusion | Covered | `SqliteDocumentCollection.HybridSearchAsync`. The weighted sum remains as an option. |
| Approximate nearest-neighbor indexes (HNSW, IVF) | Not covered | The SQLite store compares against every chunk, which suits thousands of chunks and not millions. Use the Databricks or Snowflake search tools at larger scale. |
| Learned sparse and late-interaction retrieval (SPLADE, ColBERT) | Out of scope | Model-specific indexes |
| Metadata filtering before search | Covered | `source` and `tag` filters |
| Query transformation: multi-query and RAG-Fusion | Covered | `rag_search` takes several phrasings and fuses them |
| Query transformation: step-back and decomposition | Partial | The model writes the phrasings it passes to `rag_search`, and the tool description asks it to. There is no separate rewriting step. |
| HyDE | Not covered | |
| Reranking | Covered | `LlmReranker`. A cross-encoder reranker is not provided. |
| Contextual compression | Not covered | |
| Self-RAG | Partial | The model decides whether to call `rag_search`, with `Agent:Rag:AutoInject` off. The reflection tokens are a training technique. |
| Corrective RAG (grade, then fall back to web search) | Not covered | |
| Adaptive RAG (route by query complexity) | Partial | `AutoInject` off lets the model skip retrieval for simple turns |
| Graph RAG | Not covered | |
| REFRAG decoding | Out of scope | Model-serving technique |
| Agentic RAG: iterative, multi-hop | Covered | The model calls `rag_search` repeatedly inside the agent loop. The loop guard bounds the rounds. |
| Multi-source routing | Partial | The model chooses between `rag_search`, the vector search tools and others by their descriptions. There is no router component. |
| Tool-augmented RAG | Covered | The same tool loop |
| Search-R1 and RAFT (RL and fine-tuning for retrieval) | Out of scope | Training |
| Retrieval metrics: Recall@K, MRR, NDCG | Not covered | |
| Generation metrics: faithfulness, answer relevance, RAGAs | Not covered | `EvalTests` grades relevance, coherence and fluency, and has no faithfulness evaluator |
| Embedding model choice | Covered | Provider settings. The `Local` provider takes any model the server hosts. |
| Pre-filtering, streaming | Covered | Filters and AG-UI |
| Embedding cache | Partial | Tool-selection vectors are cached. Query embeddings for RAG are not. |
| Parallel retrieval from several sources | Not covered | |
| Incremental indexing, versioning, expiry | Covered | Replacement by source with a version number, `Agent:Rag:TimeToLiveDays` |
| Embedding model drift | Partial | Documented. Changing the model means indexing again. The store does not record which model produced a vector. |
| Citations | Covered | Numbered passages, and sources in `rag_search` results |
| Lost-in-the-middle, over-retrieval | Partial | A small `TopK` by default |
| Context poisoning and prompt injection through documents | Partial | Writes to the index go through an authorized endpoint and no agent tool writes. The `rag_search` description tells the model results are data. There is no content filter on retrieved text. |
| Joint retriever and generator training | Out of scope | Training |

## Chapter 17: Agentic memory

| Concept | Status | Where or what is missing |
|---|---|---|
| Working memory | Covered | The context window under `ContextBudgetChatClient` |
| Episodic memory | Covered | `EpisodicMemoryStore` |
| Semantic memory | Partial | Context entries and entities hold facts as Markdown. There is no graph. |
| Procedural memory | Covered | Skills and workflows |
| Embedding-based recall | Covered | `search_memory` ranks by similarity when an embedding model is set |
| Summarization memory | Covered | The budget summarizes dropped history |
| Hierarchical compression | Not covered | |
| Graph memory, GraphRAG, temporal knowledge graphs | Not covered | |
| Key-value memory networks | Out of scope | Neural architecture |
| MemGPT-style self-directed memory | Covered | The model calls `search_memory`, `save_to_memory` and `forget_memory` |
| MemGPT page-in and page-out tiers | Partial | The context window and the store are two tiers. There is no explicit promotion policy. |
| Write filter by importance | Covered | `Importance` against `ImportanceThreshold` |
| Contradiction detection | Not covered | |
| Memory granularity | Partial | Episodes and entities. There is no atomic-fact store. |
| Temporal decay | Covered | `MemoryDecayService`, slower for important episodes |
| Recency-weighted retrieval | Partial | Recent episodes are injected. Relevance decays. The similarity score is not multiplied by recency. |
| Duplicate suppression and consolidation | Partial | Near-duplicates merge. Clustering and summarizing related episodes is not done. |
| Forgetting: decay, pruning | Covered | `ApplyDecay` prunes below 0.01 |
| Spaced repetition | Covered | A recall refreshes an episode |
| Reflection that writes insights to semantic memory | Partial | `ReflectiveChatClient` prompts reflection. Nothing stores the insight. |
| User modeling and preferences | Covered | User profiles |
| Session continuity | Covered | Chat history, episodic memory |
| Personalization | Partial | Profiles are injected. There is no adaptive verbosity. |
| Shared memory across agents | Partial | Stores are shared and episodes carry an agent name for filtering |
| Blackboard | Partial | Shared stores and the agent mailbox |
| Conflict resolution in shared memory | Not covered | |
| Privacy: inspect and delete | Covered | `/api/memory/episodes`, `forget_memory` |
| Privacy: consent, per-user access control | Not covered | Access control is per endpoint key |
| RL for memory operations | Out of scope | Training |
| Memory benchmarks (LongMemEval) | Not covered | |
| A-MEM, Mem0 | Not covered | Automatic fact extraction and linked notes |
| Sleep-time compute | Out of scope | A scheduled job could run it. The platform ships none. |
| Proactive memory agent | Out of scope | Research architecture |

## Chapter 18: Agent harness

| Concept | Status | Where or what is missing |
|---|---|---|
| Separation of reasoning, execution, memory, communication and observability | Covered | The layer structure |
| Prompt budget with a tokenizer, per-component ceilings | Covered | `ContextBudgetChatClient` |
| Dynamic allocation across components | Not covered | Ceilings are fixed fractions |
| Summarization of old turns | Covered | |
| Selective retention by relevance, importance-weighted truncation | Not covered | |
| Sliding window with pinned messages | Covered | The task and system messages stay |
| Hierarchical summarization | Not covered | |
| Recursive context decomposition | Not covered | Handoff to a sub-agent is the nearest piece |
| Pre-flight token check and overflow handling | Covered | |
| Modular prompt assembly | Covered | Context providers |
| Prompt registry with versions | Partial | Personas and skills are files in version control |
| Few-shot management | Not covered | |
| Tool description guidance | Covered | The tool design guidelines in Platform Internals |
| Tool schemas for several vendors | Covered | `Microsoft.Extensions.AI` |
| Forced tool choice | Partial | Available through chat options in code |
| Parallel tool calls | Covered | Framework behavior |
| Retrieval-based tool selection | Covered | `Agent:ToolSelection`, off by default |
| Fine-tuned tool selection | Out of scope | Training |
| Tool output truncation | Covered | `Agent:ToolOutput:MaxChars` |
| Error normalization | Partial | Governance and the framework return errors as text |
| Tool retry with backoff | Partial | Connector HTTP retries safe methods. Other tools do not retry. |
| Execution isolation | Partial | Allowlisted paths and commands run without a shell. There is no container sandbox. |
| Input sanitization, audit logging | Covered | Governance, the audit log |
| Prompt injection through tool output | Partial | Vision and RAG tools mark results as data. There is no general output filter. |
| MCP client and server | Covered | |
| ReAct loop | Covered | The harness agent |
| Plan and execute | Partial | The todo list and Magentic planning |
| Supervisor, peer-to-peer, swarm | Covered | Magentic, messaging, handoff |
| Hierarchical agents | Partial | Handoff can nest. There is no tree builder. |
| Approval gates | Covered | Interrupts, approval-required tools |
| Escalation rule on confidence, irreversibility or spend | Partial | Approvals and cost caps. No confidence threshold. |
| Workflow graphs | Covered | Workflows, orchestration |
| Conversation, task and persistent state | Covered | Chat history, checkpoints, SQLite |
| Rollback of actions | Not covered | |
| Retry on transient model errors | Partial | Provider SDK defaults |
| Fallback models | Not covered | |
| Loop detection | Covered | `LoopGuardChatClient` |
| Graceful failure report | Covered | The loop guard asks the model to report |
| Traces, logs, metrics | Covered | OpenTelemetry spans and meters, the run ledger |
| Replay of past runs | Not covered | |
| Streaming | Covered | AG-UI |
| Prompt caching | Not covered | Provider feature |
| Speculative tool execution | Not covered | |
| Token budgets, cost tracking, model routing | Covered | |
| Caching tool results | Not covered | |
| Rate limiting | Covered | Per-caller limits |
| Priority queues and backpressure | Partial | The trigger queue is bounded and durable |
| A/B tests, canary, shadow runs | Not covered | |
| LLM-as-judge evaluation | Covered | `EvalTests` with a separate judge model |

## Chapter 19: Loop engineering

| Concept | Status | Where or what is missing |
|---|---|---|
| Automations: schedules, webhooks, file events | Covered | Scheduler, event triggers |
| Worktree isolation for parallel agents | Out of scope | A coding-agent practice |
| Skills as persistent conditioning | Covered | |
| Connectors | Covered | |
| Maker-checker separation | Covered | `Review` orchestration |
| External state between iterations | Covered | Run ledger, orchestration store, episodic memory |
| Validation loop with a deterministic verifier | Partial | A Review checker agent can run a test tool and judge from the result. There is no verifier port for code. |
| Reflexion loop | Partial | Reflection prompts and episodic memory, not tied into a retry loop |
| Evaluator-optimizer loop | Covered | `Review` |
| Hierarchical loop | Covered | Magentic and handoff |
| Autonomous research loop with commit and rollback | Out of scope | |
| Verification hierarchy | Partial | The checker may be deterministic through tools |
| Termination: iteration cap, token budget, cost budget | Covered | `LoopGuard` |
| Termination: no-progress detection | Covered | Identical results, repeated calls, unchanged revisions |
| Termination: fatal error | Partial | A failed run is recorded as failed |
| Escalation to a human | Partial | Approvals and notifications. A stuck run reports to the user and sends no alert. |
| Exploration: temperature escalation | Not covered | |
| Exploration: strategy memory | Partial | Episodic memory |
| Exploration: fresh sub-agent | Partial | Handoff starts a new session |
| Reward hacking defenses | Partial | Governance and approvals limit what a loop can change |
| Context degradation countermeasures | Covered | Compaction, tool output cap, sub-agent sessions |
| Cost model and budgets | Covered | `Agent:Budgets` |
| Prompt caching economics | Not covered | |

## Chapter 20: Agent design patterns

| Concept | Status | Where |
|---|---|---|
| Prompt chaining | Covered | `Sequential`, workflows |
| Routing | Partial | A lead agent in a handoff routes. There is no standalone classifier. |
| Parallelization: sectioning | Covered | `Concurrent` |
| Parallelization: voting | Partial | `Concurrent` returns every answer. Counting the votes is left to the caller. |
| Orchestrator-workers | Covered | Magentic |
| Evaluator-optimizer | Covered | `Review` |
| ReAct | Covered | |
| Planning agents with replanning | Covered | Magentic |
| Reflection and Reflexion | Partial | See chapter 19 |
| Single, multi, sequential and nested tool use | Covered | |
| Fallback tool use | Not covered | |
| Structured outputs | Covered | `StructuredOutput` |
| Start simple, transparency, plan for failure | Covered | Design stance of the runtime |
| Pattern selection guide | Covered | Orchestration guide |

## Chapter 21: Environments and benchmarks

This chapter is about training and benchmarking agents, which the platform does not do.

| Concept | Status | Where or what is missing |
|---|---|---|
| Sandboxed code execution | Partial | Allowlisted commands without a shell. No container. |
| Reproducible evaluation in source | Covered | `EvalTests` |
| Trajectory logging | Covered | The run ledger records tool calls, usage and outcome |
| Environment versioning | Partial | Seeds and configuration are files |
| Gym-style `reset` and `step` environments, OpenEnv | Out of scope | |
| Reward design, curriculum, adaptive difficulty | Out of scope | |
| WebArena, OSWorld, SWE-bench and similar | Out of scope | |
| Parallel rollout collection | Out of scope | |
| Held-out environments, human baselines | Out of scope | |
| Multi-agent environments | Out of scope | |
| Agent evaluation harness design | Covered | `EvalTests` |
| Checkpointing long episodes | Covered | Orchestration checkpoints |

## Chapter 22: Model Context Protocol

| Concept | Status | Where or what is missing |
|---|---|---|
| Client over streamable HTTP | Covered | `McpConnectionManager`, the MCP connector |
| Client over stdio | Not covered | A local MCP server that speaks only stdio needs an HTTP wrapper |
| Server over streamable HTTP | Covered | `Features:McpServer` |
| Tools | Covered | |
| Resources | Not covered | |
| Prompts | Not covered | |
| Sampling | Not covered | |
| Dynamic tool registration | Covered | The dynamic tool registry |
| Tool annotations | Covered | The MCP connector classifies risk from them, and the connection manager asks approval for `destructiveHint` tools |
| Multiple servers | Covered | |
| Reconnection and health checks | Covered | A health timer reconnects |
| Capability negotiation, JSON-RPC, progress, cancellation | Covered | The MCP SDK |
| Trust boundaries, user consent | Covered | Governance and approval |
| Prompt injection through resources | Partial | Servers' tools pass through governance. Text is not filtered. |
| Input validation, SSRF | Covered | The guarded connector HTTP client |
| Credential management | Covered | The encrypted connection store |
| Sandboxing of servers | Partial | The host does not start servers |
| MCP for RL training | Out of scope | |
| Trajectory recording for fine-tuning | Out of scope | |

## Chapter 23: Agent skills

| Concept | Status | Where or what is missing |
|---|---|---|
| Skill as prompt, tools and knowledge | Partial | A skill is a prompt template. Tools are separate. |
| Static loading | Covered | |
| Dynamic discovery and routing | Partial | The model lists and picks skills. There is no semantic router. |
| Hierarchical composition | Not covered | |
| Manifest with version and dependencies | Not covered | |
| Lifecycle: activate, deactivate | Partial | A skill runs when called |
| Authoring at runtime | Covered | `SkillAuthoring` |
| Registry | Covered | `LocalSkillRegistry` |
| Permission model per agent | Covered | Tool seeds |
| Skills versus fine-tuning | Out of scope | Conceptual |
| Workflow and agent patterns as skill templates | Covered | |
| Marketplace | Not covered | |

## Chapter 24: Agent-to-agent communication

| Concept | Status | Where or what is missing |
|---|---|---|
| Agent cards, task lifecycle, streaming | Covered | The A2A server library |
| Push notifications | Not covered | Not wired by the platform |
| Authentication on the A2A endpoint | Covered | Endpoint keys, API key and JWT |
| mTLS, scoped authorization per skill | Not covered | |
| Request and response, streaming, multi-turn | Covered | |
| Broadcast | Partial | Messaging to several agents |
| Publish and subscribe | Partial | Event triggers |
| Negotiation, auction, contract net | Not covered | |
| Agent registry | Covered | `AgentRegistry`, remote agents by URL |
| Capability-based routing | Partial | The model reads agent descriptions |
| Load balancing, version management | Not covered | |
| Context scoping | Partial | Handoff passes the task, not the history |
| Correlation identifiers | Partial | Child runs roll up to the parent run in the ledger |
| Blackboard | Partial | |
| Consensus protocols | Not covered | |
| A2A together with MCP | Covered | |
| Audit trail of delegations | Covered | Run ledger, audit log |
| Identity verification of agents | Partial | Caller identity from authentication |
| Message signing, end-to-end encryption | Not covered | |

## Chapter 25: Multi-agent systems

| Concept | Status | Where or what is missing |
|---|---|---|
| Supervisor topology | Covered | Magentic, handoff |
| Peer-to-peer | Covered | Messaging |
| Hierarchical | Partial | Handoff can nest |
| Swarm with handoffs | Covered | |
| Shared state | Partial | Shared stores |
| Message passing | Covered | The mailbox |
| Task DAG | Covered | Magentic plan, workflows |
| Voting and consensus | Partial | `Concurrent` gathers the votes |
| Market-based allocation | Not covered | |
| Stigmergy | Partial | Shared stores and files |
| Structured message performatives | Not covered | |
| Context sharing strategies | Partial | Summaries by the budget |
| Role design, personas | Covered | Personas |
| Dynamic role reassignment | Partial | `switch_persona` |
| Debate | Partial | A group chat with opposing personas |
| Reflection pattern | Covered | `Review` |
| Division of labor, pipeline, ensemble | Covered | `Concurrent`, `Sequential` |
| Teacher-student | Not covered | |
| Red team | Partial | A Review run with an adversarial checker persona |
| Multi-agent RL (CTDE, self-play, population training) | Out of scope | |
| Coordination overhead limits | Covered | `MaxAgents`, round limits, cost in the ledger |
| Attribution | Partial | Per-run cost and tool calls |
| Safety monitor agent | Not covered | |
| Multi-level evaluation metrics | Partial | Run ledger totals |
| Self-modifying BDI agents | Out of scope | |

The chapter warns about agents that delete their own safety constraints. The guardrail tools let an agent create, change, toggle and delete guardrails when asked in chat. A host that treats guardrails as fixed policy should add those tool names to `Security:ApprovalRequiredTools`, which blocks them, or leave them out of the agent's tool seed.

## Chapter 26: Agent development frameworks

The chapter compares other frameworks. AI Agent Canvas builds on Microsoft Agent Framework, which is one of the frameworks the book surveys, so the comparison is out of scope. The lifecycle and operations sections apply.

| Concept | Status | Where or what is missing |
|---|---|---|
| Unit tests for tools | Covered | `tests/AiAgentCanvas.Tests` |
| Integration tests of whole loops with a scripted model | Covered | `ScriptedChatClient` |
| Golden-trajectory regression tests | Not covered | |
| Behavioral and adversarial tests | Partial | Governance and loop guard tests |
| Cost and latency tests | Partial | Meters exist. No gate on them. |
| Tracing | Covered | OpenTelemetry |
| Failure categorization | Partial | Run outcome and termination reason |
| Replay | Not covered | |
| Async execution | Covered | Scheduler, triggers, jobs |
| Multi-tenant isolation | Partial | A session isolation key for chat history. No tenant boundary for data. |
| Cost optimization | Covered | Router, budgets, caps |
| Auto-scaling | Not covered | One process |
| Interoperability standards | Covered | A2A, MCP, AG-UI |
| Framework comparisons (LangGraph, CrewAI, AutoGen, DSPy and others) | Out of scope | |

## Chapter 27: Agentic UI

| Concept | Status | Where or what is missing |
|---|---|---|
| Chat interface | Covered | |
| Canvas and artifact views | Partial | The state panel |
| Workflow visualization | Partial | Orchestration transcripts |
| Dashboards and monitoring | Covered | The Runs tab |
| Collaborative editing | Not covered | |
| Autonomous work with checkpoints | Covered | Approvals, plan sign-off |
| Reasoning display | Covered | Reasoning blocks |
| Tool use visualization | Covered | |
| Progress indicators | Covered | |
| Approval gates | Covered | |
| Context display | Not covered | |
| Error and recovery UI | Partial | Health banner |
| Confidence indicators | Not covered | |
| Streaming of tokens and tool calls | Covered | AG-UI events |
| Multi-agent streaming | Partial | Per-agent messages in the transcript |
| Backpressure | Covered | SSE |
| Generative UI | Not covered | |
| Tiered approval workflows | Partial | Approve or block, with no tiers |
| Feedback and teaching through the UI | Not covered | |
| Explaining decisions, undo and rollback | Not covered | |
| Audit trail in the UI | Partial | The Runs tab shows runs. The audit log has an API. |
| Managing expectations | Out of scope | Product design |
| UI framework comparisons | Out of scope | |
| Optimistic updates | Out of scope | |

## Left for later, and why

- **Graph memory and Graph RAG.** They need an entity extraction pass and a graph store, a new storage dependency.
- **Corrective RAG, HyDE and contextual compression.** Each adds a model call to every retrieval. The model can already issue several phrasings through `rag_search`, which gets most of the benefit for the cases that need it.
- **Evaluation for retrieval and memory.** Recall@K, faithfulness and LongMemEval-style tests need labeled data that belongs to the deployment.
- **stdio MCP clients.** Starting a process from model-supplied input needs an allowlist like the one the system tools use. It deserves its own design.
- **MCP resources, prompts and sampling.** The SDK supports them. Nothing in the host would consume them yet.
- **Contradiction detection and memory consolidation by clustering.** Both need a judgement call from a model on every write.
- **Replay of past runs, golden-trajectory tests, canary and shadow runs.** These are operational tooling that depends on how a deployment ships.
