using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.Providers.Databricks;

public static class DatabricksServiceExtensions
{
    public static IServiceCollection AddDatabricks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DatabricksOptions>(configuration.GetSection(DatabricksOptions.SectionName));
        services.AddSingleton<DatabricksClientFactory>();
        services.AddSingleton<IChatClient>(sp =>
            sp.GetRequiredService<DatabricksClientFactory>().CreateChatClient());

        return services;
    }

    public static IServiceCollection AddDatabricksEmbeddings(
        this IServiceCollection services)
    {
        services.AddEmbeddingGenerator<string, Embedding<float>>(sp =>
        {
            var generator = sp.GetRequiredService<DatabricksClientFactory>().CreateEmbeddingGenerator();
            return generator ?? throw new InvalidOperationException(
                "EmbeddingGenerator requires Databricks:EmbeddingModelName to be configured in appsettings.json.");
        });

        return services;
    }
}
