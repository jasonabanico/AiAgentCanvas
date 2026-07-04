> [Developer Guide](developer-guide.md) > RAG Pipeline

# Developer Guide: RAG Pipeline

AI Agent Canvas includes a full Retrieval-Augmented Generation (RAG) pipeline that enriches every agent response with relevant documents from a vector store. The pipeline goes beyond basic vector search with **recursive chunking**, **hybrid search** (vector + keyword), **metadata filtering**, **LLM-based reranking**, and **citation attribution**.

## Pipeline Overview

```
Document Ingestion                          Query Pipeline
================                          ==============

Raw Document                               User Message
    |                                          |
    v                                          v
DocumentChunker                            Embed Query
(recursive split by                        (IEmbeddingGenerator)
 paragraph -> sentence)                        |
    |                                          v
    v                                     Hybrid Search
Embed Each Chunk                          (vector cosine similarity
(IEmbeddingGenerator)                      + FTS5 keyword/BM25)
    |                                          |
    v                                          v
Store in SQLite                           Metadata Filtering
(vector BLOB + FTS5                       (source, tags)
 full-text index)                              |
                                               v
                                          LLM Reranking
                                          (top-10 -> top-3)
                                               |
                                               v
                                          Citation Formatting
                                          ([1] source: X, score: 0.82)
                                               |
                                               v
                                          Inject into Agent Context
```

## Document Chunking

The `DocumentChunker` service in `AiAgentCanvas.Core` recursively splits documents into chunks suitable for embedding. Rather than using fixed-size character windows, it follows a semantic hierarchy:

1. **Split by paragraphs** first (double newlines)
2. If a paragraph exceeds `ChunkSize` (default 512 chars), **split by sentences** (period/exclamation/question followed by space or newline)
3. Apply **overlap** (default 64 chars) between consecutive chunks for context continuity
4. Discard chunks under 20 characters

```csharp
public sealed class DocumentChunker
{
    public int ChunkSize { get; init; } = 512;
    public int ChunkOverlap { get; init; } = 64;

    public List<DocumentChunk> Chunk(string text, string? source = null)
    {
        // 1. Split by paragraphs
        // 2. If paragraph > ChunkSize, split by sentences
        // 3. Apply overlap between consecutive chunks
        // 4. Filter out chunks < 20 chars
    }
}
```

Each chunk is returned as a `DocumentChunk` with the text, a sequential index, and an optional source identifier. The source flows through to the `DocumentRecord` for citation tracking.

### Configuration

| Property | Default | Description |
|----------|---------|-------------|
| `ChunkSize` | 512 | Maximum characters per chunk |
| `ChunkOverlap` | 64 | Characters of overlap between consecutive chunks |

## Hybrid Search

The SQLite vector store supports **hybrid search** that combines two retrieval signals:

- **Vector search** (cosine similarity) -- captures semantic meaning
- **Keyword search** (SQLite FTS5 / BM25) -- captures exact term matches

At ingestion time, each document is stored in both the main table (with the embedding BLOB) and an FTS5 full-text index. At query time, both scores are computed and combined:

```
finalScore = (vectorWeight * cosineSimilarity) + (keywordWeight * bm25Score)
```

Default weights are **70% vector, 30% keyword**. This means semantic similarity dominates, but exact keyword matches get a meaningful boost -- particularly useful when users search for specific terms, product names, or technical jargon that embedding models may not represent precisely.

### FTS5 Integration

The FTS5 virtual table is created alongside the main documents table:

```sql
CREATE VIRTUAL TABLE IF NOT EXISTS [documents_fts]
USING fts5(id UNINDEXED, text, content=[documents], content_rowid=rowid)
```

Query terms are sanitized and joined with OR for broad matching. The BM25 rank is normalized to a 0-1 scale using `1 / (1 + |rank|)`.

### IHybridSearchable Interface

The hybrid search capability is exposed through `IHybridSearchable` in Abstractions, keeping Core decoupled from the SQLite implementation:

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

The `RagContextProvider` checks if the collection implements `IHybridSearchable` and uses it when available, falling back to standard vector-only search otherwise.

## Metadata Filtering

`DocumentRecord` now includes two filterable fields:

| Field | Type | Filter | Example |
|-------|------|--------|---------|
| `Source` | string? | Exact match | `"annual-report-2024.pdf"` |
| `Tags` | string? | Contains (LIKE) | `"finance,quarterly,earnings"` |

Filters are applied as SQL WHERE clauses **before** vector scoring, reducing the candidate set and improving both relevance and performance:

```csharp
var options = new RagSearchOptions
{
    SourceFilter = "annual-report-2024.pdf",  // exact match
    TagFilter = "finance",                     // LIKE '%finance%'
    KeywordQuery = "revenue breakdown",        // FTS5 keyword boost
    VectorWeight = 0.7f,
    KeywordWeight = 0.3f,
};
```

## LLM-Based Reranking

After retrieval, the `LlmReranker` uses the same LLM to re-score candidates by relevance. This is a **cross-encoder pattern** implemented via prompting -- the LLM sees both the query and each candidate together, which produces better relevance judgments than embedding similarity alone.

### How It Works

1. Retrieve top-10 candidates via hybrid search
2. Send the query + all 10 candidates (truncated to 300 chars each) to the LLM
3. Ask the LLM to return a JSON array ranking the candidates by relevance
4. Take the top-3 from the reranked list
5. If the LLM call fails, fall back to the original ranking

```csharp
public async Task<List<VectorSearchResult<DocumentRecord>>> RerankAsync(
    string query,
    List<VectorSearchResult<DocumentRecord>> candidates,
    int topN = 3,
    CancellationToken ct = default)
{
    if (candidates.Count <= topN)
        return candidates;

    // Build prompt: "Given query X, rank these chunks [0]-[9] by relevance"
    // Parse JSON response: [2, 0, 7, ...] -> reorder candidates
    // Fallback: return candidates.Take(topN) on any error
}
```

### Cost Considerations

Reranking adds one extra LLM call per user query. The call uses `MaxOutputTokens=100` and `Temperature=0` to keep it fast and deterministic. For latency-sensitive deployments, the reranker is optional -- remove it from DI registration to skip this step.

## Citation and Attribution

The `RagContextProvider` formats each retrieved chunk with a numbered citation that includes the source and relevance score:

```
Relevant context from documents (cite by number when using):

[1] (source: annual-report-2024.pdf, score: 0.892)
Revenue increased 12% year-over-year to $4.2B, driven by cloud services
growth in the enterprise segment...

---

[2] (source: earnings-call-q4.pdf, score: 0.847)
Management highlighted three strategic priorities for the coming fiscal
year: AI infrastructure, developer tooling, and...

---

[3] (source: market-analysis.pdf, score: 0.791)
Industry analysts project 15-20% growth in the enterprise AI platform
market through 2026...
```

The LLM is instructed to "cite by number when using" these documents. This produces responses like:

> Revenue grew 12% to $4.2B [1], with management focusing on AI infrastructure and developer tooling as key growth areas [2]. This aligns with industry projections of 15-20% growth in enterprise AI platforms [3].

## DocumentRecord Schema

```csharp
public sealed class DocumentRecord
{
    [VectorStoreKey]
    public string Id { get; set; } = string.Empty;

    [VectorStoreData]
    public string Text { get; set; } = string.Empty;

    [VectorStoreData]
    public string? Source { get; set; }        // e.g. "report.pdf"

    [VectorStoreData]
    public string? Tags { get; set; }          // e.g. "finance,quarterly"

    [VectorStoreData]
    public string? MetadataJson { get; set; }  // arbitrary JSON

    [VectorStoreVector(1536)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}
```

## Service Registration

RAG is opt-in. Set `AIFoundry:EmbeddingDeploymentName` in appsettings.json to enable:

```csharp
// Program.cs -- conditional registration
if (!string.IsNullOrEmpty(builder.Configuration["AIFoundry:EmbeddingDeploymentName"]))
{
    builder.Services.AddSqliteVectorStore(builder.Configuration);
    builder.Services.AddAiAgentCanvasRag();
}

// AddAiAgentCanvasRag() registers:
// - IEmbeddingGenerator (from Azure AI Foundry)
// - DocumentChunker (recursive text splitter)
// - LlmReranker (LLM-based reranking)
// - RagContextProvider (hybrid search + citations)
```

## Ingestion Example

To ingest a document using the chunker and vector store:

```csharp
var chunker = sp.GetRequiredService<DocumentChunker>();
var collection = sp.GetRequiredService<VectorStoreCollection<string, DocumentRecord>>();
var embedder = sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

var chunks = chunker.Chunk(documentText, source: "annual-report-2024.pdf");

foreach (var chunk in chunks)
{
    var embedding = await embedder.GenerateVectorAsync(chunk.Text);
    await collection.UpsertAsync(new DocumentRecord
    {
        Id = $"report-2024-{chunk.Index}",
        Text = chunk.Text,
        Source = chunk.Source,
        Tags = "finance,annual-report",
        Embedding = embedding.Vector,
    });
}
```

## Architecture Summary

| Component | Location | Purpose |
|-----------|----------|---------|
| `DocumentRecord` | Abstractions | DTO with Id, Text, Source, Tags, Embedding |
| `DocumentChunk` | Abstractions | Output of chunking: Text, Index, Source |
| `RagSearchOptions` | Abstractions | Filters and weights for hybrid search |
| `IHybridSearchable` | Abstractions | Interface for hybrid vector + keyword search |
| `DocumentChunker` | Core | Recursive paragraph/sentence text splitter |
| `LlmReranker` | Core | LLM-based candidate reranking |
| `RagContextProvider` | Core | Orchestrates search, rerank, citation, injection |
| `SqliteDocumentCollection` | VectorStore.Sqlite | SQLite + FTS5 hybrid search implementation |

---

