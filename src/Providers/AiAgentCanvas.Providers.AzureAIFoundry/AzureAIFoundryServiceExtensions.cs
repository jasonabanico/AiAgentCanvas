using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.Providers.AzureAIFoundry;

public static class AzureAIFoundryServiceExtensions
{
    public static IServiceCollection AddAzureAIFoundry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AzureAIFoundryOptions>(configuration.GetSection(AzureAIFoundryOptions.SectionName));
        services.AddSingleton<AzureAIFoundryClientFactory>();
        services.AddSingleton<IChatClient>(sp =>
            sp.GetRequiredService<AzureAIFoundryClientFactory>().CreateChatClient());

        RegisterSecondaryClient(services, configuration, AgentClientKeys.Economy, "EconomyDeploymentName");
        RegisterSecondaryClient(services, configuration, AgentClientKeys.Judge, "JudgeDeploymentName");

        return services;
    }

    public static IServiceCollection AddAzureAIFoundryEmbeddings(
        this IServiceCollection services)
    {
        services.AddEmbeddingGenerator<string, Embedding<float>>(sp =>
        {
            var generator = sp.GetRequiredService<AzureAIFoundryClientFactory>().CreateEmbeddingGenerator();
            return generator ?? throw new InvalidOperationException(
                "EmbeddingGenerator requires AIFoundry:EmbeddingDeploymentName to be configured in appsettings.json.");
        });

        return services;
    }

    /// <summary>
    /// Registers a keyed chat client only when its deployment is configured, so
    /// consumers can tell "not set up" from "set up and unavailable".
    /// </summary>
    private static void RegisterSecondaryClient(
        IServiceCollection services, IConfiguration configuration, string key, string settingName)
    {
        var deployment = configuration[$"{AzureAIFoundryOptions.SectionName}:{settingName}"];
        if (string.IsNullOrWhiteSpace(deployment))
            return;

        services.AddKeyedSingleton<IChatClient>(key, (sp, _) =>
            sp.GetRequiredService<AzureAIFoundryClientFactory>().CreateChatClient(deployment));
    }
}
