using AiAgentCanvas.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.AgentData.Entities;

public static class EntityServiceExtensions
{
    private const string DefaultRoot = "./agent-data/orchestrator";
    private const string DefaultSharedRoot = "./agent-data/shared";

    private static string[] SharedDirs(string sharedRoot, string domain) =>
    [
        Path.Combine(sharedRoot, "agent", domain),
        Path.Combine(sharedRoot, "user", domain),
    ];

    public static IServiceCollection AddAiAgentCanvasEntities(
        this IServiceCollection services,
        string rootDirectory = DefaultRoot,
        string sharedRootDirectory = DefaultSharedRoot)
    {
        var store = new EntityStore(
            Path.Combine(rootDirectory, "agent", "entities"),
            Path.Combine(rootDirectory, "user", "entities"),
            SharedDirs(sharedRootDirectory, "entities"));
        services.AddSingleton(store);
        services.AddSingleton<EntityToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<EntityToolProvider>().GetTools());
        services.AddSingleton<AIContextProvider>(sp =>
        {
            foreach (var seed in sp.GetServices<IEntitySeed>())
            {
                if (store.Get(seed.Name) is null)
                    store.Save(seed.Name, seed.Type, seed.Tags, seed.Content);
            }
            return new EntityContextProvider(store);
        });
        return services;
    }
}
