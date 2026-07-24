using AiAgentCanvas.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.AgentData.Workflows;

public static class WorkflowServiceExtensions
{
    private const string DefaultRoot = "./agent-data/orchestrator";
    private const string DefaultSharedRoot = "./agent-data/shared";

    private static string[] SharedDirs(string sharedRoot, string domain) =>
    [
        Path.Combine(sharedRoot, "agent", domain),
        Path.Combine(sharedRoot, "user", domain),
    ];

    public static IServiceCollection AddAiAgentCanvasWorkflows(
        this IServiceCollection services,
        string rootDirectory = DefaultRoot,
        string sharedRootDirectory = DefaultSharedRoot)
    {
        var store = new WorkflowStore(
            Path.Combine(rootDirectory, "agent", "workflows"),
            Path.Combine(rootDirectory, "user", "workflows"),
            SharedDirs(sharedRootDirectory, "workflows"));
        services.AddSingleton(store);
        services.AddSingleton<WorkflowExecutor>();
        services.AddSingleton(sp => new DeclarativeWorkflowExecutor(
            Path.Combine(rootDirectory, "agent", "declarative-workflows"),
            sp,
            sp.GetRequiredService<ILogger<DeclarativeWorkflowExecutor>>()));
        services.AddSingleton<WorkflowToolProvider>();
        services.AddSingleton<DeclarativeWorkflowToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
        {
            foreach (var seed in sp.GetServices<IWorkflowSeed>())
            {
                if (store.Get(seed.Name) is null)
                    store.Save(seed.Name, seed.Description, seed.Tags, seed.Content);
            }
            return sp.GetRequiredService<WorkflowToolProvider>().GetTools();
        });
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<DeclarativeWorkflowToolProvider>().GetTools());

        services.AddSingleton(new ToolStateMapping("list_workflows", ToolStateBehavior.Snapshot));
        services.AddSingleton(new ToolStateMapping("run_workflow", ToolStateBehavior.Delta));
        services.AddSingleton(new ToolStateMapping("run_sequential_workflow", ToolStateBehavior.Delta));
        services.AddSingleton(new ToolStateMapping("run_concurrent_workflow", ToolStateBehavior.Delta));

        return services;
    }
}
