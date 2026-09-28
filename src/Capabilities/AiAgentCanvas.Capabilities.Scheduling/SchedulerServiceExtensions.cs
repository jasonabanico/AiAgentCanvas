using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.Capabilities.Scheduling;

public static class SchedulerServiceExtensions
{
    public static IServiceCollection AddAiAgentCanvasScheduler(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var options = new SchedulerOptions();
        configuration?.GetSection(SchedulerOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        services.AddSingleton<ScheduledAgentJob>();
        services.AddSingleton<SchedulerToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<SchedulerToolProvider>().GetTools());

        // Without this the scheduling tools persist rows that nothing executes.
        services.AddHostedService<ScheduledTaskRunner>();

        services.AddSingleton(new ToolStateMapping("list_scheduled_tasks", ToolStateBehavior.Snapshot));

        return services;
    }
}
