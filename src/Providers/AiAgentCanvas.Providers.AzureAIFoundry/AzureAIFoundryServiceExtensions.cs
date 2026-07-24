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
}
