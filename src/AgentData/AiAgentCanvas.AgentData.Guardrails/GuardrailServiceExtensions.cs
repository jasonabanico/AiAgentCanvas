using AiAgentCanvas.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.AgentData.Guardrails;

public static class GuardrailServiceExtensions
{
    private const string DefaultRoot = "./agent-data/orchestrator";
    private const string DefaultSharedRoot = "./agent-data/shared";

    private static string[] SharedDirs(string sharedRoot, string domain) =>
    [
        Path.Combine(sharedRoot, "agent", domain),
        Path.Combine(sharedRoot, "user", domain),
    ];

    public static IServiceCollection AddAiAgentCanvasGuardrails(
        this IServiceCollection services,
        string rootDirectory = DefaultRoot,
        string sharedRootDirectory = DefaultSharedRoot)
    {
        var store = new GuardrailStore(
            Path.Combine(rootDirectory, "agent", "guardrails"),
            Path.Combine(rootDirectory, "user", "guardrails"),
            SharedDirs(sharedRootDirectory, "guardrails"));
        services.AddSingleton(store);
        services.AddSingleton<GuardrailToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<GuardrailToolProvider>().GetTools());
        services.AddSingleton<AIContextProvider>(sp =>
        {
            foreach (var seed in sp.GetServices<IGuardrailSeed>())
            {
                if (store.Get(seed.Name) is null)
                    store.Save(seed.Name, seed.Severity, seed.Enabled, seed.Rule);
            }
            return new GuardrailContextProvider(store);
        });
        return services;
    }
}
