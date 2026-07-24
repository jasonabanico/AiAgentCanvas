using AiAgentCanvas.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.AgentData.Profiles;

public static class ProfileServiceExtensions
{
    private const string DefaultRoot = "./agent-data/orchestrator";
    private const string DefaultSharedRoot = "./agent-data/shared";

    private static string[] SharedDirs(string sharedRoot, string domain) =>
    [
        Path.Combine(sharedRoot, "agent", domain),
        Path.Combine(sharedRoot, "user", domain),
    ];

    public static IServiceCollection AddAiAgentCanvasUserProfiles(
        this IServiceCollection services,
        string rootDirectory = DefaultRoot,
        string sharedRootDirectory = DefaultSharedRoot)
    {
        var store = new UserProfileStore(
            Path.Combine(rootDirectory, "agent", "profiles"),
            Path.Combine(rootDirectory, "user", "profiles"),
            SharedDirs(sharedRootDirectory, "profiles"));
        services.AddSingleton(store);
        services.AddSingleton<UserProfileToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<UserProfileToolProvider>().GetTools());
        services.AddSingleton<AIContextProvider>(sp =>
        {
            foreach (var seed in sp.GetServices<IUserProfileSeed>())
            {
                if (store.Get(seed.Name) is null)
                    store.Save(seed.Name, seed.Role, seed.Timezone, seed.Content);
            }
            return new UserProfileContextProvider(store);
        });
        return services;
    }
}
