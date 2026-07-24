using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace DataConnection.VectorSearch.Databricks;

public static class DatabricksVectorSearchServiceExtensions
{
    /// <summary>
    /// Registers the <c>databricks_vector_search</c> agent tool. Additive: the local SQLite
    /// vector store remains the default RAG store. Call only when the tool is configured
    /// (see <see cref="DatabricksVectorSearchOptions.IsConfigured"/>).
    /// </summary>
    public static IServiceCollection AddDatabricksVectorSearchTool(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DatabricksVectorSearchOptions>(
            configuration.GetSection(DatabricksVectorSearchOptions.SectionName));

        services.AddHttpClient("DatabricksVectorSearch", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddStandardResilienceHandler();

        services.AddSingleton<DatabricksVectorSearchToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<DatabricksVectorSearchToolProvider>().GetTools());

        services.AddSingleton(new ToolStateMapping("databricks_vector_search", ToolStateBehavior.Snapshot));

        return services;
    }
}
