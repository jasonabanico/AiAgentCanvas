using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace DataConnection.VectorSearch.Snowflake;

public static class SnowflakeCortexSearchServiceExtensions
{
    /// <summary>
    /// Registers the <c>snowflake_cortex_search</c> agent tool. Additive: the local SQLite
    /// vector store remains the default RAG store. Call only when the tool is configured
    /// (see <see cref="SnowflakeCortexSearchOptions.IsConfigured"/>).
    /// </summary>
    public static IServiceCollection AddSnowflakeCortexSearchTool(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SnowflakeCortexSearchOptions>(
            configuration.GetSection(SnowflakeCortexSearchOptions.SectionName));

        services.AddHttpClient("SnowflakeCortexSearch", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddStandardResilienceHandler();

        services.AddSingleton<SnowflakeCortexSearchToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<SnowflakeCortexSearchToolProvider>().GetTools());

        services.AddSingleton(new ToolStateMapping("snowflake_cortex_search", ToolStateBehavior.Snapshot));

        return services;
    }
}
