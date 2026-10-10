namespace AiAgentCanvas.Capabilities.Rag;

public sealed class RagOptions
{
    public const string SectionName = "Agent:Rag";

    /// <summary>Passages kept after reranking.</summary>
    public int TopK { get; set; } = 3;

    /// <summary>Candidates fetched before reranking. Wide enough for recall, since the reranker narrows it.</summary>
    public int RetrieveK { get; set; } = 10;

    /// <summary>
    /// Search on every user message and add the best passages to the agent's instructions. Turn
    /// it off to let the agent decide when to search by calling <c>rag_search</c>, which costs
    /// nothing on turns that need no documents.
    /// </summary>
    public bool AutoInject { get; set; } = true;

    /// <summary>Rerank candidates with a model call. Turn it off to save that call.</summary>
    public bool Rerank { get; set; } = true;

    /// <summary>Characters per chunk. A factual lookup does better with 300 to 500, a summary with 1000 to 2000.</summary>
    public int ChunkSize { get; set; } = 512;

    public int ChunkOverlap { get; set; } = 64;

    /// <summary>The largest document one ingest request may carry.</summary>
    public int MaxDocumentChars { get; set; } = 1_000_000;

    /// <summary>Chunks sent to the embedding model in one request.</summary>
    public int EmbeddingBatchSize { get; set; } = 32;

    /// <summary>
    /// Chunks older than this many days are deleted, which keeps time-sensitive sources from
    /// answering with stale text. Zero keeps everything.
    /// </summary>
    public int TimeToLiveDays { get; set; }

    /// <summary>Most query variants one <c>rag_search</c> call may fuse.</summary>
    public int MaxQueriesPerSearch { get; set; } = 5;
}
