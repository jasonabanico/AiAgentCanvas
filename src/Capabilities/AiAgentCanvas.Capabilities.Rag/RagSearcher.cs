using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace AiAgentCanvas.Capabilities.Rag;

/// <summary>
/// One place that turns a question into ranked passages, so the context provider and the
/// <c>rag_search</c> tool retrieve the same way. A store that supports hybrid search gets the
/// vector and keyword rankings fused. Any other store falls back to vector search alone.
/// </summary>
public sealed class RagSearcher
{
    private readonly VectorStoreCollection<string, DocumentRecord> _collection;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embedder;
    private readonly LlmReranker? _reranker;
    private readonly RagOptions _options;

    public RagSearcher(
        VectorStoreCollection<string, DocumentRecord> collection,
        IEmbeddingGenerator<string, Embedding<float>> embedder,
        RagOptions options,
        LlmReranker? reranker = null)
    {
        _collection = collection;
        _embedder = embedder;
        _options = options;
        _reranker = options.Rerank ? reranker : null;
    }

    /// <summary>
    /// Searches with one or more phrasings of the question and returns the best passages.
    /// Several phrasings are searched separately and their rankings fused by rank, which is
    /// RAG-Fusion: a passage that ranks well under several phrasings rises above one that
    /// matches a single phrasing by luck.
    /// </summary>
    public async Task<List<VectorSearchResult<DocumentRecord>>> SearchAsync(
        IReadOnlyList<string> queries,
        int? topK = null,
        string? source = null,
        string? tag = null,
        CancellationToken ct = default)
    {
        var phrasings = queries.Where(q => !string.IsNullOrWhiteSpace(q)).Select(q => q.Trim()).Distinct().ToList();
        if (phrasings.Count == 0)
            return [];

        var keep = Math.Max(1, topK ?? _options.TopK);
        var rankings = new List<List<VectorSearchResult<DocumentRecord>>>();
        foreach (var phrasing in phrasings)
            rankings.Add(await SearchOneAsync(phrasing, source, tag, ct));

        var candidates = rankings.Count == 1 ? rankings[0] : Fuse(rankings, _options.RetrieveK);
        if (candidates.Count == 0)
            return [];

        return _reranker is not null
            ? await _reranker.RerankAsync(string.Join(" ; ", phrasings), candidates, keep, ct)
            : candidates.Take(keep).ToList();
    }

    private async Task<List<VectorSearchResult<DocumentRecord>>> SearchOneAsync(
        string query, string? source, string? tag, CancellationToken ct)
    {
        var embedding = await _embedder.GenerateVectorAsync(query, cancellationToken: ct);
        var results = new List<VectorSearchResult<DocumentRecord>>();

        if (_collection is IHybridSearchable hybrid)
        {
            var filters = new RagSearchOptions { KeywordQuery = query, SourceFilter = source, TagFilter = tag };
            await foreach (var (record, score) in hybrid.HybridSearchAsync(embedding, _options.RetrieveK, filters, ct))
                results.Add(new VectorSearchResult<DocumentRecord>(record, score));
        }
        else
        {
            await foreach (var result in _collection.SearchAsync(embedding, _options.RetrieveK, cancellationToken: ct))
            {
                if (source is not null && !string.Equals(result.Record.Source, source, StringComparison.Ordinal))
                    continue;
                if (tag is not null && result.Record.Tags?.Contains(tag, StringComparison.OrdinalIgnoreCase) != true)
                    continue;
                results.Add(result);
            }
        }

        return results;
    }

    /// <summary>Reciprocal rank fusion across the rankings, one per phrasing.</summary>
    internal static List<VectorSearchResult<DocumentRecord>> Fuse(
        IReadOnlyList<List<VectorSearchResult<DocumentRecord>>> rankings, int take, int rankConstant = 60)
    {
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        var records = new Dictionary<string, DocumentRecord>(StringComparer.Ordinal);

        foreach (var ranking in rankings)
        {
            for (var i = 0; i < ranking.Count; i++)
            {
                var record = ranking[i].Record;
                records[record.Id] = record;
                scores[record.Id] = scores.GetValueOrDefault(record.Id) + 1.0 / (rankConstant + i + 1);
            }
        }

        return scores
            .OrderByDescending(s => s.Value)
            .Take(take)
            .Select(s => new VectorSearchResult<DocumentRecord>(records[s.Key], s.Value))
            .ToList();
    }
}
