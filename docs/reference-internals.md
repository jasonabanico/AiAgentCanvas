# Reference: Platform Internals

## Context Provider Chain

Context providers extend `AIContextProvider` (from `Microsoft.Agents.AI`) and run in DI registration order. Each provider's `ProvideAIContextAsync` method appends content to `AIContext.Instructions`, building up the system prompt that the agent receives.

| Order | Provider | Source | What It Injects |
|-------|----------|--------|-----------------|
| 1 | `GovernanceContextProvider` | AiAgentCanvas.Security | Scans existing instructions for prompt injection; emits audit events if detected (does not modify instructions) |
| 2 | `PersonaContextProvider` | AiAgentCanvas.AgentData.Personas | Active persona's instructions, or the default system prompt if no persona is active |
| 3 | `PersistentContextProvider` | AiAgentCanvas.AgentData.Context | All saved context entries (facts, notes, preferences) |
| 4 | `EntityContextProvider` | AiAgentCanvas.AgentData.Entities | Entity index listing all known entities and their types |
| 5 | `UserProfileContextProvider` | AiAgentCanvas.AgentData.Profiles | Active user profile context (role, timezone, preferences) |
| 6 | `GuardrailContextProvider` | AiAgentCanvas.AgentData.Guardrails | All enabled guardrail rules |
| 7 | `DynamicToolContextProvider` | AiAgentCanvas.Orchestration.Skills | Injects dynamically registered tools into `AIContext.Tools` (the only provider that modifies tools rather than instructions) |
| 8 | `RagContextProvider` | AiAgentCanvas.Capabilities.Rag | Retrieves relevant document chunks via hybrid search and appends them as numbered citations (only active when RAG is configured) |

The registration order follows the order of `AddAiAgentCanvas*()` calls in `Program.cs`: Security first, then Personas, Context, Entities, Profiles, Guardrails, Skills, and finally RAG.

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
| `ISkillSeed` | `SkillSeed` | `Name`, `Description`, `PromptTemplate` | (via SkillRegistry) |
| `IUserProfileSeed` | `UserProfileSeed` | `Name`, `Role`, `Timezone`, `Content` | `./agent-data/orchestrator/agent/profiles/` |
| `IMcpConnectionSeed` | `McpConnectionSeed` | `Name`, `Endpoint`, `Transport` | (via MCP connection manager) |
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

The RAG (Retrieval-Augmented Generation) pipeline is conditionally enabled when `AIFoundry:EmbeddingDeploymentName` is configured. It adds relevant document context to the agent's system prompt before each response.

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
