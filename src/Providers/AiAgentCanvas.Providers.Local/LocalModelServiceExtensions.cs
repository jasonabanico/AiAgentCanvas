using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.Providers.Local;

public static class LocalModelServiceExtensions
{
    public static IServiceCollection AddLocalModel(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<LocalModelOptions>(configuration.GetSection(LocalModelOptions.SectionName));
        services.AddSingleton<LocalModelClientFactory>();
        services.AddSingleton<IChatClient>(sp =>
            sp.GetRequiredService<LocalModelClientFactory>().CreateChatClient());

        RegisterSecondaryClient(services, configuration, AgentClientKeys.Economy, "EconomyModelName");
        RegisterSecondaryClient(services, configuration, AgentClientKeys.Judge, "JudgeModelName");

        return services;
    }

    public static IServiceCollection AddLocalEmbeddings(this IServiceCollection services)
    {
        services.AddEmbeddingGenerator<string, Embedding<float>>(sp =>
        {
            var generator = sp.GetRequiredService<LocalModelClientFactory>().CreateEmbeddingGenerator();
            return generator ?? throw new InvalidOperationException(
                "EmbeddingGenerator requires Local:EmbeddingModelName to be configured.");
        });

        return services;
    }

    /// <summary>
    /// Registers a keyed chat client only when its model is configured, so consumers can tell
    /// "not set up" from "set up and unavailable".
    /// </summary>
    private static void RegisterSecondaryClient(
        IServiceCollection services, IConfiguration configuration, string key, string settingName)
    {
        var model = configuration[$"{LocalModelOptions.SectionName}:{settingName}"];
        if (string.IsNullOrWhiteSpace(model))
            return;

        services.AddKeyedSingleton<IChatClient>(key, (sp, _) =>
            sp.GetRequiredService<LocalModelClientFactory>().CreateChatClient(model));
    }
}
