using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.Scheduler;

public static class SchedulerServiceExtensions
{
    public static IServiceCollection AddAiAgentCanvasScheduler(
        this IServiceCollection services,
        string connectionString = "Data Source=scheduler.db",
        Action<AutonomousExecutionOptions>? configureAutonomous = null)
    {
        var autonomousOptions = new AutonomousExecutionOptions();
        configureAutonomous?.Invoke(autonomousOptions);

        services.AddSingleton(new ScheduledTaskStore(connectionString));
        services.AddSingleton(autonomousOptions);
        services.AddSingleton<ScheduledAgentJob>();
        services.AddSingleton<AutonomousAgentJob>();
        services.AddSingleton<SchedulerToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<SchedulerToolProvider>().GetTools());
        return services;
    }
}
