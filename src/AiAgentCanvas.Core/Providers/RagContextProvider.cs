using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Core.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace AiAgentCanvas.Core.Providers;

public sealed class RagContextProvider : AIContextProvider
{
    private readonly VectorStoreCollection<string, DocumentRecord> _collection;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly LlmReranker? _reranker;
    private readonly ILogger<RagContextProvider> _logger;
    private readonly int _topK;
    private readonly int _retrieveK;

    public RagContextProvider(
        VectorStoreCollection<string, DocumentRecord> collection,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        ILogger<RagContextProvider> logger,
        LlmReranker? reranker = null,
        int topK = 3,
        int retrieveK = 10)
    {
        _collection = collection;
        _embeddingGenerator = embeddingGenerator;
        _logger = logger;
        _reranker = reranker;
        _topK = topK;
        _retrieveK = retrieveK;
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken)
    {
        var lastUserMessage = context.AIContext.Messages?
            .LastOrDefault(m => m.Role == ChatRole.User)?.Text;

        if (string.IsNullOrWhiteSpace(lastUserMessage))
            return context.AIContext;

        _logger.LogDebug("RAG search for: {Query}", lastUserMessage);

        var queryEmbedding = await _embeddingGenerator.GenerateVectorAsync(lastUserMessage, cancellationToken: cancellationToken);

        var candidates = new List<VectorSearchResult<DocumentRecord>>();

        if (_collection is IHybridSearchable hybridStore)
        {
            var ragOptions = new RagSearchOptions { KeywordQuery = lastUserMessage };
            await foreach (var (record, score) in hybridStore.HybridSearchAsync(queryEmbedding, _retrieveK, ragOptions, cancellationToken))
                candidates.Add(new VectorSearchResult<DocumentRecord>(record, score));
        }
        else
        {
            await foreach (var result in _collection.SearchAsync(queryEmbedding, _retrieveK, cancellationToken: cancellationToken))
                candidates.Add(result);
        }

        if (candidates.Count == 0)
        {
            _logger.LogDebug("No RAG results found");
            return context.AIContext;
        }

        var results = _reranker is not null
            ? await _reranker.RerankAsync(lastUserMessage, candidates, _topK, cancellationToken)
            : candidates.Take(_topK).ToList();

        _logger.LogInformation("RAG returned {Retrieved} candidates, using {Used} after reranking", candidates.Count, results.Count);

        var citations = results.Select((r, i) =>
        {
            var source = r.Record.Source ?? "unknown";
            var score = r.Score?.ToString("F3") ?? "n/a";
            var preview = r.Record.Text.Length > 200 ? r.Record.Text[..200] + "..." : r.Record.Text;
            return $"[{i + 1}] (source: {source}, score: {score})\n{preview}";
        });

        var ragContext = string.Join("\n\n---\n\n", citations);
        context.AIContext.Instructions += $"""

            Relevant context from documents (cite by number when using):
            {ragContext}
            """;
        return context.AIContext;
    }
}
