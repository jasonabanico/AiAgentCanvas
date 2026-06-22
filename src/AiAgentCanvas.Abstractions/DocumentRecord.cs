using Microsoft.Extensions.VectorData;

namespace AiAgentCanvas.Abstractions;

public sealed class DocumentRecord
{
    [VectorStoreKey]
    public string Id { get; set; } = string.Empty;

    [VectorStoreData]
    public string Text { get; set; } = string.Empty;

    [VectorStoreData]
    public string? Source { get; set; }

    [VectorStoreData]
    public string? Tags { get; set; }

    [VectorStoreData]
    public string? MetadataJson { get; set; }

    [VectorStoreVector(1536, DistanceFunction = Microsoft.Extensions.VectorData.DistanceFunction.CosineSimilarity)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}

public sealed class DocumentChunk
{
    public string Text { get; set; } = string.Empty;
    public int Index { get; set; }
    public string? Source { get; set; }
}

public sealed class RagSearchOptions
{
    public string? SourceFilter { get; set; }
    public string? TagFilter { get; set; }
    public string? KeywordQuery { get; set; }
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
