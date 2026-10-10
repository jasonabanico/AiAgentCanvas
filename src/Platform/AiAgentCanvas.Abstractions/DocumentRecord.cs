using Microsoft.Extensions.VectorData;

namespace AiAgentCanvas.Abstractions;

public sealed class DocumentRecord
{
    [VectorStoreKey]
    public string Id { get; set; } = string.Empty;

    [VectorStoreData]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Names the document this chunk belongs to. Re-indexing a source replaces every chunk
    /// that carries it, so a document keeps one identity across its versions.
    /// </summary>
    [VectorStoreData]
    public string? Source { get; set; }

    [VectorStoreData]
    public string? Tags { get; set; }

    [VectorStoreData]
    public string? MetadataJson { get; set; }

    /// <summary>When the chunk was written. Null for chunks stored before this field existed.</summary>
    [VectorStoreData]
    public DateTimeOffset? IndexedAt { get; set; }

    /// <summary>Starts at 1 and rises each time the source is indexed again.</summary>
    [VectorStoreData]
    public int Version { get; set; } = 1;

    [VectorStoreVector(1536, DistanceFunction = Microsoft.Extensions.VectorData.DistanceFunction.CosineSimilarity)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}

public sealed class DocumentChunk
{
    public string Text { get; set; } = string.Empty;
    public int Index { get; set; }
    public string? Source { get; set; }
}

/// <summary>How a hybrid search combines the vector ranking with the keyword ranking.</summary>
public enum RagFusion
{
    /// <summary>
    /// Reciprocal rank fusion: each ranking contributes 1 / (k + rank). It uses ranks and not
    /// scores, because a cosine similarity and a keyword rank are not on the same scale.
    /// </summary>
    ReciprocalRank,

    /// <summary>A weighted sum of the cosine similarity and a keyword score. Kept for callers that tuned the weights.</summary>
    WeightedScore,
}

public sealed class RagSearchOptions
{
    public string? SourceFilter { get; set; }
    public string? TagFilter { get; set; }
    public string? KeywordQuery { get; set; }
    public RagFusion Fusion { get; set; } = RagFusion.ReciprocalRank;

    /// <summary>The smoothing constant in reciprocal rank fusion. 60 is the value the method was published with.</summary>
    public int RankConstant { get; set; } = 60;

    public float KeywordWeight { get; set; } = 0.3f;
    public float VectorWeight { get; set; } = 0.7f;
}

public interface IHybridSearchable
{
    IAsyncEnumerable<(DocumentRecord Record, double? Score)> HybridSearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int top,
        RagSearchOptions? options = null,
        CancellationToken ct = default);
}

/// <summary>One indexed document: every chunk that shares a source.</summary>
public sealed record IndexedDocument(string Source, int Chunks, int Version, DateTimeOffset? IndexedAt, string? Tags);

/// <summary>
/// Document-level operations on the index. A vector store keys its records by chunk, but a
/// person thinks in documents: replace this policy, delete that one, expire what is old.
/// </summary>
public interface IDocumentIndex
{
    Task<IReadOnlyList<IndexedDocument>> ListDocumentsAsync(CancellationToken ct = default);

    /// <summary>The newest version stored for the source, or 0 when it has none.</summary>
    Task<int> LatestVersionAsync(string source, CancellationToken ct = default);

    /// <summary>Removes every chunk of the source and returns how many were removed.</summary>
    Task<int> DeleteDocumentAsync(string source, CancellationToken ct = default);

    /// <summary>Removes chunks of the source older than <paramref name="version"/>.</summary>
    Task<int> DeleteOlderVersionsAsync(string source, int version, CancellationToken ct = default);

    /// <summary>Removes chunks indexed before the cutoff, whatever their source.</summary>
    Task<int> DeleteIndexedBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
