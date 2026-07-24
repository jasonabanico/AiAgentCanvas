using AiAgentCanvas.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace AiAgentCanvas.Capabilities.Rag;

public static class RagServiceExtensions
{
    public static IServiceCollection AddAiAgentCanvasRag(this IServiceCollection services)
    {
        services.AddSingleton<DocumentChunker>();
        services.AddSingleton<LlmReranker>();
        services.AddSingleton<AIContextProvider>(sp =>
            new RagContextProvider(
                sp.GetRequiredService<VectorStoreCollection<string, DocumentRecord>>(),
                sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
                sp.GetRequiredService<ILogger<RagContextProvider>>(),
                sp.GetRequiredService<LlmReranker>()));
        return services;
    }
}
