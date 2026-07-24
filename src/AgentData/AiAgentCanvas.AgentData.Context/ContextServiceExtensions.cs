using AiAgentCanvas.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.AgentData.Context;

public static class ContextServiceExtensions
{
    private const string DefaultRoot = "./agent-data/orchestrator";
    private const string DefaultSharedRoot = "./agent-data/shared";

    private static string[] SharedDirs(string sharedRoot, string domain) =>
    [
        Path.Combine(sharedRoot, "agent", domain),
        Path.Combine(sharedRoot, "user", domain),
    ];

    public static IServiceCollection AddAiAgentCanvasContext(
        this IServiceCollection services,
        string rootDirectory = DefaultRoot,
        string sharedRootDirectory = DefaultSharedRoot)
    {
        var store = new ContextStore(
            Path.Combine(rootDirectory, "agent", "context"),
            Path.Combine(rootDirectory, "user", "context"),
            SharedDirs(sharedRootDirectory, "context"));
        services.AddSingleton(store);
        services.AddSingleton<ContextToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<ContextToolProvider>().GetTools());
        services.AddSingleton<AIContextProvider>(sp =>
        {
            foreach (var seed in sp.GetServices<IContextSeed>())
            {
                if (store.Get(seed.Topic) is null)
                    store.Save(seed.Topic, seed.Type, seed.Tags, seed.Content);
            }
            return new PersistentContextProvider(store);
        });
        return services;
    }
}
