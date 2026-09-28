using AiAgentCanvas.Abstractions;
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

        RegisterSecondaryClient(services, configuration, AgentClientKeys.Economy, "EconomyModelName");
        RegisterSecondaryClient(services, configuration, AgentClientKeys.Judge, "JudgeModelName");

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

    /// <summary>
    /// Registers a keyed chat client only when its serving endpoint is configured, so
    /// consumers can tell "not set up" from "set up and unavailable".
    /// </summary>
    private static void RegisterSecondaryClient(
        IServiceCollection services, IConfiguration configuration, string key, string settingName)
    {
        var model = configuration[$"{DatabricksOptions.SectionName}:{settingName}"];
        if (string.IsNullOrWhiteSpace(model))
            return;

        services.AddKeyedSingleton<IChatClient>(key, (sp, _) =>
            sp.GetRequiredService<DatabricksClientFactory>().CreateChatClient(model));
    }
}
