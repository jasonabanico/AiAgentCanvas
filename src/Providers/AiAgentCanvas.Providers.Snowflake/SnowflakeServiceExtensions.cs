using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.Providers.Snowflake;

public static class SnowflakeServiceExtensions
{
    public static IServiceCollection AddSnowflake(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SnowflakeOptions>(configuration.GetSection(SnowflakeOptions.SectionName));
        services.AddSingleton<SnowflakeClientFactory>();
        services.AddSingleton<IChatClient>(sp =>
            sp.GetRequiredService<SnowflakeClientFactory>().CreateChatClient());

        return services;
    }

    public static IServiceCollection AddSnowflakeEmbeddings(
        this IServiceCollection services)
    {
        services.AddEmbeddingGenerator<string, Embedding<float>>(sp =>
        {
            var generator = sp.GetRequiredService<SnowflakeClientFactory>().CreateEmbeddingGenerator();
            return generator ?? throw new InvalidOperationException(
                "EmbeddingGenerator requires Snowflake:EmbeddingModelName to be configured in appsettings.json.");
        });

        return services;
    }
}
