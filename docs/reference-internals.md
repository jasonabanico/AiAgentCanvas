# Reference: Platform Internals

## Context Provider Chain

Context providers extend `AIContextProvider` (from `Microsoft.Agents.AI`) and run in DI registration order. Each provider's `ProvideAIContextAsync` method appends content to `AIContext.Instructions`, building up the system prompt that the agent receives.

| Order | Provider | Source | What It Injects |
|-------|----------|--------|-----------------|
| 1 | `GovernanceContextProvider` | AiAgentCanvas.Security | Scans existing instructions for prompt injection and emits audit events if it finds any. It does not modify instructions. |
| 2 | `SystemPromptProvider` | AiAgentCanvas.Orchestration | The default system prompt |
| 3 | `PersonaContextProvider` | AiAgentCanvas.AgentData.Personas | The active persona's instructions |
| 4 | `PersistentContextProvider` | AiAgentCanvas.AgentData.Context | All saved context entries (facts, notes, preferences) |
| 5 | `GuardrailContextProvider` | AiAgentCanvas.AgentData.Guardrails | All enabled guardrail rules |
| 6 | `UserProfileContextProvider` | AiAgentCanvas.AgentData.Profiles | The active user profile (role, timezone, preferences) |
| 7 | `EntityContextProvider` | AiAgentCanvas.AgentData.Entities | The entity index listing known entities and their types |
| 8 | `RagContextProvider` | AiAgentCanvas.Capabilities.Rag | Relevant document chunks from hybrid search, as numbered citations. Present only when RAG is configured. |
| 9 | `EpisodicMemoryContextProvider` | AiAgentCanvas.Capabilities.EpisodicMemory | Recent episodes. Present when `EpisodicMemory` is on. |

The order follows the order of the registration calls in `Program.cs`: Security first, then the core runtime, then Personas, Context, Guardrails, UserProfiles and Entities, then RAG and episodic memory. A provider is present only when its flag is on.

---

## Chat Pipeline

`AgentPipeline` builds the chat client chain and wraps tools. The default agent, each persona agent built by the registry, and the structured responder use it. Listed from the outside in:

| Client | Present when | Notes |
|--------|--------------|-------|
| `LoopGuardChatClient` | `Agent:LoopGuard:Enabled` (default true) | Tool-round cap, token budget, optional `MaxRunCost`, repeat and stagnation detection |
| `ReflectiveChatClient` | `Agent:Reflection:Enabled` | Reflection prompt after consecutive tool rounds |
| `AuditingChatClient` | `AuditLog` flag | Records each model call |
| `CostAwareModelRouter` | `Agent:ModelRouter:Enabled` | Needs an economy model |
| `ContextBudgetChatClient` | `Agent:ContextBudget:Enabled` (default true) | Counts with the tokenizer named in `Agent:TokenizerModel` |
| `CostTrackingChatClient` | Always on | Prices from `Agent:Pricing`. An unpriced model reports tokens and no cost. |
| `ToolDeduplicatingChatClient` | Always on | Closest to the provider |

`AgentPipeline.WrapTools` wraps each `AIFunction` in the governance wrapper when one is registered, then in `TracedAIFunction`. Tools that are not functions pass through unchanged.

### Run Tracking

`RunTracking.RunAsync` gives a unit of work an ambient `AgentRunContext` and, when a ledger is registered, a run record. Scheduled agent tasks, trigger events, handoffs, jobs, orchestration runs and outside MCP calls use it. The context collects usage from the cost-tracking client and tool calls from the tracing wrapper, rolls a child's usage up to its parent, and feeds the per-run cost cap in the loop guard. The `BudgetGuard` reads the ledger to decide whether a new unattended run may start.

---

## Known Gaps

These are places where the code does less than its surrounding design suggests. Each has a follow-up task.

- **Runtime tools do not reach agents.** `DynamicToolRegistry` holds tools added at runtime: those from `connect_mcp_server`, and those from connectors. `DynamicToolContextProvider` is written to deliver them but is not registered, and nothing else reads the registry. Tools registered at startup are unaffected.
- **The rate limiter is not applied.** `Security:RateLimitPerMinute` registers a fixed-window policy named `agent`, and `UseRateLimiter` is in the pipeline, but no endpoint calls `RequireRateLimiting("agent")`.

---

## MarkdownFile Utility

`MarkdownFile` (`AiAgentCanvas.Abstractions` namespace) is a utility for reading and writing markdown files with YAML frontmatter. All agent data domains use it for persistence.

### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `Parse` | `static MarkdownFile? Parse(string filePath)` | Reads a file from disk and parses its frontmatter and body. Returns `null` if the file does not contain valid frontmatter delimiters (`---`). |
| `ParseContent` | `static MarkdownFile? ParseContent(string content, string filePath)` | Parses frontmatter and body from a string. Returns `null` if the content does not start with `---` or lacks a closing delimiter. |
| `Write` | `static void Write(string filePath, Dictionary<string, string> frontmatter, string body)` | Creates the parent directory if needed, serializes frontmatter as `key: value` lines between `---` delimiters, appends the body, and writes UTF-8. |
| `LoadAll` | `static List<MarkdownFile> LoadAll(string directory, string pattern = "*.md")` | Returns all parseable markdown files from a directory. |
| `SanitizeFileName` | `static string SanitizeFileName(string name)` | Lowercases the name and replaces spaces and underscores with hyphens. |
| `Get` | `string Get(string key, string fallback = "")` | Looks up a frontmatter key; returns `fallback` if missing. |
| `GetBool` | `bool GetBool(string key, bool fallback = false)` | Looks up a frontmatter key and parses it as a boolean. |

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Frontmatter` | `Dictionary<string, string>` | Parsed key-value pairs from the YAML frontmatter block |
| `Body` | `string` | Everything after the closing `---` delimiter |
| `FilePath` | `string` | The file path this instance was parsed from |

---

## Tool Design Guidelines

Follow these rules when building tools for AI Agent Canvas:

1. **Use descriptive names matching the domain.** Tool names should read naturally: `stock_quote`, `list_personas`, `run_workflow`. Avoid generic names like `execute` or `process`.

2. **Write clear `[Description]` attributes.** The description is included in the model's prompt. Be specific about what the tool does, what it returns, and when to use it.

3. **Return structured JSON.** Tools should return JSON-serializable objects. The AG-UI protocol and state panel depend on structured output.

4. **Handle errors gracefully.** Return error information as part of the result rather than throwing exceptions. The model can interpret and relay error messages to the user.

5. **Keep tools focused.** Each tool should do one job. Prefer two small tools over one tool with a `mode` parameter.

6. **Accept `CancellationToken`.** Pass the token through to all async operations. This allows the system to cancel work when the user disconnects.

7. **Document parameters with `[Description]`.** Every parameter should have a description attribute so the model knows what values to pass.

---

## Seed Interface Reference

Seeds provide default data for each agent domain. They are resolved from DI at startup. If the corresponding file does not already exist on disk, the seed data is persisted. Seeds never overwrite files that a user has manually edited.

| Interface | Concrete Type | Properties | Persistence Path |
|-----------|--------------|------------|------------------|
| `IPersonaSeed` | `PersonaSeed` | `Name`, `Description`, `Instructions` | `./agent-data/orchestrator/agent/personas/` |
| `IContextSeed` | `ContextSeed` | `Topic`, `Type`, `Tags`, `Content` | `./agent-data/orchestrator/agent/context/` |
| `IWorkflowSeed` | `WorkflowSeed` | `Name`, `Description`, `Tags`, `Content` | `./agent-data/orchestrator/agent/workflows/` |
| `IEntitySeed` | `EntitySeed` | `Name`, `Type`, `Tags`, `Content` | `./agent-data/orchestrator/agent/entities/` |
| `IGuardrailSeed` | `GuardrailSeed` | `Name`, `Severity`, `Enabled`, `Rule` | `./agent-data/orchestrator/agent/guardrails/` |
| `ISkillSeed` | `SkillSeed` | `Name`, `Description`, `PromptTemplate` | `./agent-data/skills/` |
| `IUserProfileSeed` | `UserProfileSeed` | `Name`, `Role`, `Timezone`, `Content` | `./agent-data/orchestrator/agent/profiles/` |
| `IMcpConnectionSeed` | `McpConnectionSeed` | `Name`, `Endpoint`, `Transport`, `BearerToken`, `ApiKey`, `ExpectedIssuer`, `AdditionalHeaders` | (via MCP connection manager) |
| `IAgentToolsSeed` | `AgentToolsSeed` | `AgentName`, `ToolNames` (list) | (in-memory tool assignment) |
| `IGoalSeed` | `GoalSeed` | `Name`, `Description`, `Priority`, `AcceptanceCriteria`, `AssignedAgent`, `Content` | `./agent-data/orchestrator/agent/goals/` |

Each domain also supports user-created data that persists under the `user/` subtree (e.g., `./agent-data/orchestrator/user/personas/`) and shared data under `./agent-data/shared/`.

### Seed Behavior

1. At startup, the DI container resolves all registered `ISeed` implementations for each domain.
2. For each seed, the store checks if a file with the sanitized name already exists.
3. If the file does not exist, the seed data is written using `MarkdownFile.Write()`.
4. If the file exists, the seed is skipped -- manual edits are preserved.

---

## RAG Pipeline Internals

The RAG (Retrieval-Augmented Generation) pipeline is enabled when the `Rag` feature flag is on and the active provider has an embedding model: `AIFoundry:EmbeddingDeploymentName` for Azure AI Foundry, or `Databricks:EmbeddingModelName` for Databricks. It adds relevant document context to the agent's system prompt before each response.

### DocumentChunker

The `DocumentChunker` class splits text into overlapping chunks for ingestion.

| Parameter | Default | Description |
|-----------|---------|-------------|
| `ChunkSize` | 512 | Maximum characters per chunk |
| `ChunkOverlap` | 64 | Characters of overlap carried from the previous chunk |

The chunking algorithm:

1. Split text by double-newline (paragraph boundaries).
2. Accumulate paragraphs into a buffer until `ChunkSize` would be exceeded.
3. Flush the buffer as a `DocumentChunk`, carrying the last `ChunkOverlap` characters forward.
4. If a single paragraph exceeds `ChunkSize`, split it further by sentence endings (`. `, `! `, `? `).
5. Discard chunks shorter than 20 characters.

### Hybrid Search

Hybrid search combines vector similarity with keyword matching. The weights are configurable via `RagSearchOptions`:

| Component | Weight | Method |
|-----------|--------|--------|
| Vector search | 0.7 (default) | Cosine similarity against stored embeddings |
| Keyword search | 0.3 (default) | SQLite FTS5 BM25 ranking |

The `IHybridSearchable` interface:

```csharp
public interface IHybridSearchable
{
    IAsyncEnumerable<(DocumentRecord Record, double? Score)> HybridSearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int top,
        RagSearchOptions? options = null,
        CancellationToken ct = default);
}
```

`RagSearchOptions` fields: `SourceFilter`, `TagFilter`, `KeywordQuery`, `KeywordWeight` (default 0.3f), `VectorWeight` (default 0.7f).

The final score for each document is: `(VectorWeight * vectorScore) + (KeywordWeight * keywordScore)`.

### FTS5 Schema

The SQLite FTS5 virtual table is created alongside the main documents table:

```sql
CREATE VIRTUAL TABLE IF NOT EXISTS [{collection}_fts]
USING fts5(id UNINDEXED, text, content=[{collection}], content_rowid=rowid)
```

- `id` is stored but not indexed (marked `UNINDEXED`).
- `text` is the searchable column.
- The FTS table is a content-sync table that mirrors the main documents table.

Keyword scores are normalized as `1.0 / (1.0 + |rank|)` where `rank` is the FTS5 BM25 score.

### LLM Reranking

After hybrid search retrieves the top candidates, an LLM reranker narrows the results:

1. Retrieve `retrieveK` candidates (default: 10) via hybrid search.
2. Truncate each candidate's text to 300 characters.
3. Send a prompt to the LLM asking it to return a JSON array of chunk indices ranked by relevance (e.g., `[2, 0, 4, 1, 3]`).
4. Parse the JSON array, deduplicate indices, and take the top `topK` (default: 3).
5. On any parse or LLM error, fall back to the original ordering truncated to `topK`.

LLM reranking parameters: `Temperature = 0`, `MaxOutputTokens = 100`.

### DocumentRecord

```csharp
public sealed class DocumentRecord
{
    [VectorStoreKey]
    public string Id { get; set; }

    [VectorStoreData]
    public string Text { get; set; }

    [VectorStoreData]
    public string? Source { get; set; }

    [VectorStoreData]
    public string? Tags { get; set; }

    [VectorStoreData]
    public string? MetadataJson { get; set; }

    [VectorStoreVector(1536, DistanceFunction = CosineSimilarity)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}
```

The embedding dimension is 1536 (compatible with OpenAI text-embedding-ada-002 and similar models). The `VectorStoreVector` attribute specifies cosine similarity as the distance function.
