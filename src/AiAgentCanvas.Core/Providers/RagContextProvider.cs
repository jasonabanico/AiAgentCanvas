using AiAgentCanvas.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace AiAgentCanvas.Core.Providers;

public sealed class RagContextProvider : AIContextProvider
{
    private readonly VectorStoreCollection<string, DocumentRecord> _collection;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ILogger<RagContextProvider> _logger;
    private readonly int _topK;

    public RagContextProvider(
        VectorStoreCollection<string, DocumentRecord> collection,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        ILogger<RagContextProvider> logger,
        int topK = 5)
    {
        _collection = collection;
        _embeddingGenerator = embeddingGenerator;
        _logger = logger;
        _topK = topK;
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken)
    {
        var lastUserMessage = context.AIContext.Messages?
            .LastOrDefault(m => m.Role == ChatRole.User)?.Text;

        if (string.IsNullOrWhiteSpace(lastUserMessage))
            return context.AIContext;

        _logger.LogDebug("RAG search for: {Query}", lastUserMessage);

        var queryEmbedding = await _embeddingGenerator.GenerateVectorAsync(lastUserMessage, cancellationToken: cancellationToken);
        var results = new List<string>();

        await foreach (var result in _collection.SearchAsync(queryEmbedding, _topK, cancellationToken: cancellationToken))
        {
            results.Add(result.Record.Text);
        }

        if (results.Count == 0)
        {
            _logger.LogDebug("No RAG results found");
            return context.AIContext;
        }

        _logger.LogInformation("RAG returned {Count} results", results.Count);

        var ragContext = string.Join("\n\n---\n\n", results);
        context.AIContext.Instructions += $"\n\nRelevant context from documents:\n{ragContext}";
        return context.AIContext;
    }
}
