using AiAgentCanvas.Abstractions;
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

        RegisterSecondaryClient(services, configuration, AgentClientKeys.Economy, "EconomyModelName");
        RegisterSecondaryClient(services, configuration, AgentClientKeys.Judge, "JudgeModelName");

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

    /// <summary>
    /// Registers a keyed chat client only when its Cortex model is configured, so
    /// consumers can tell "not set up" from "set up and unavailable".
    /// </summary>
    private static void RegisterSecondaryClient(
        IServiceCollection services, IConfiguration configuration, string key, string settingName)
    {
        var model = configuration[$"{SnowflakeOptions.SectionName}:{settingName}"];
        if (string.IsNullOrWhiteSpace(model))
            return;

        services.AddKeyedSingleton<IChatClient>(key, (sp, _) =>
            sp.GetRequiredService<SnowflakeClientFactory>().CreateChatClient(model));
    }
}
