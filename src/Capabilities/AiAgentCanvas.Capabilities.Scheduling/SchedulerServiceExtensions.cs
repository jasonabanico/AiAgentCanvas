using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.Capabilities.Scheduling;

public static class SchedulerServiceExtensions
{
    public static IServiceCollection AddAiAgentCanvasScheduler(
        this IServiceCollection services)
    {
        services.AddSingleton<ScheduledAgentJob>();
        services.AddSingleton<SchedulerToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<SchedulerToolProvider>().GetTools());

        services.AddSingleton(new ToolStateMapping("list_scheduled_tasks", ToolStateBehavior.Snapshot));

        return services;
    }
}
