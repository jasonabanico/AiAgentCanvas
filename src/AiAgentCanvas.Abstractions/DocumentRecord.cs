using Microsoft.Extensions.VectorData;

namespace AiAgentCanvas.Abstractions;

public sealed class DocumentRecord
{
    [VectorStoreKey]
    public string Id { get; set; } = string.Empty;

    [VectorStoreData]
    public string Text { get; set; } = string.Empty;

    [VectorStoreData]
    public string? MetadataJson { get; set; }

    [VectorStoreVector(1536, DistanceFunction = Microsoft.Extensions.VectorData.DistanceFunction.CosineSimilarity)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}
