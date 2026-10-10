using AiAgentCanvas.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace AiAgentCanvas.Capabilities.Rag;

public static class RagServiceExtensions
{
    public static IServiceCollection AddAiAgentCanvasRag(this IServiceCollection services, IConfiguration? configuration = null)
    {
        var options = new RagOptions();
        configuration?.GetSection(RagOptions.SectionName).Bind(options);

        services.AddSingleton(options);
        services.AddSingleton(new DocumentChunker { ChunkSize = options.ChunkSize, ChunkOverlap = options.ChunkOverlap });
        services.AddSingleton<LlmReranker>();

        services.AddSingleton(sp => new RagSearcher(
            sp.GetRequiredService<VectorStoreCollection<string, DocumentRecord>>(),
            sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
            sp.GetRequiredService<RagOptions>(),
            sp.GetRequiredService<LlmReranker>()));

        if (options.AutoInject)
        {
            services.AddSingleton<AIContextProvider>(sp => new RagContextProvider(
                sp.GetRequiredService<RagSearcher>(),
                sp.GetRequiredService<ILogger<RagContextProvider>>()));
        }

        // The index port is registered by the store. A store that cannot list, replace and
        // expire documents still searches, but it cannot be managed or searched by tool.
        if (services.Any(d => d.ServiceType == typeof(IDocumentIndex)))
        {
            services.AddSingleton(sp => new RagIngestionService(
                sp.GetRequiredService<VectorStoreCollection<string, DocumentRecord>>(),
                sp.GetRequiredService<IDocumentIndex>(),
                sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
                sp.GetRequiredService<DocumentChunker>(),
                sp.GetRequiredService<RagOptions>(),
                sp.GetRequiredService<ILogger<RagIngestionService>>()));

            services.AddSingleton<IReadOnlyList<AITool>>(sp => RagToolProvider.CreateTools(
                sp.GetRequiredService<RagSearcher>(),
                sp.GetRequiredService<IDocumentIndex>(),
                sp.GetRequiredService<RagOptions>()));

            if (options.TimeToLiveDays > 0)
                services.AddHostedService<RagExpiryService>();
        }

        return services;
    }
}
