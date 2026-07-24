using AiAgentCanvas.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.AgentData.Personas;

public static class PersonaServiceExtensions
{
    private const string DefaultRoot = "./agent-data/orchestrator";
    private const string DefaultSharedRoot = "./agent-data/shared";

    private static string[] SharedDirs(string sharedRoot, string domain) =>
    [
        Path.Combine(sharedRoot, "agent", domain),
        Path.Combine(sharedRoot, "user", domain),
    ];

    public static IServiceCollection AddAiAgentCanvasPersonas(
        this IServiceCollection services,
        string rootDirectory = DefaultRoot,
        string sharedRootDirectory = DefaultSharedRoot)
    {
        var store = new PersonaStore(
            Path.Combine(rootDirectory, "agent", "personas"),
            Path.Combine(rootDirectory, "user", "personas"),
            SharedDirs(sharedRootDirectory, "personas"));
        services.AddSingleton(store);
        services.AddSingleton<PersonaToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<PersonaToolProvider>().GetTools());
        services.AddSingleton<AIContextProvider>(sp =>
        {
            foreach (var seed in sp.GetServices<IPersonaSeed>())
            {
                if (store.GetPersona(seed.Name) is null)
                    store.SavePersona(seed.Name, seed.Description, seed.Instructions);
            }

            var defaultPrompt = sp.GetRequiredService<DefaultSystemPrompt>().Value;
            return new PersonaContextProvider(store, defaultPrompt);
        });
        return services;
    }
}
