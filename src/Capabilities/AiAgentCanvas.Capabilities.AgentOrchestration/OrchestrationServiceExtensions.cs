using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Orchestration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.AgentOrchestration;

public static class OrchestrationServiceExtensions
{
    /// <summary>
    /// Adds group chat, handoff and Magentic runs with durable checkpoints. The agents come
    /// from the agent registry, so the inter-agent capability must be on.
    /// </summary>
    public static IServiceCollection AddAiAgentCanvasAgentOrchestration(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var options = new OrchestrationOptions();
        configuration?.GetSection(OrchestrationOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        services.AddSingleton(sp =>
        {
            var path = Path.IsPathRooted(options.DatabasePath)
                ? options.DatabasePath
                : Path.Combine(Directory.GetCurrentDirectory(), options.DatabasePath);
            return new OrchestrationStore(path);
        });

        services.AddSingleton(sp =>
        {
            var registry = sp.GetRequiredService<AgentRegistry>();
            return new OrchestrationRunner(
                sp.GetRequiredService<OrchestrationStore>(),
                name => registry.Resolve(name),
                sp.GetRequiredService<OrchestrationOptions>(),
                sp.GetRequiredService<ILogger<OrchestrationRunner>>(),
                sp.GetService<INotificationSink>(),
                sp.GetService<IRunLedger>());
        });

        services.AddHostedService<OrchestrationMaintenanceService>();

        services.AddSingleton<IReadOnlyList<AITool>>(sp => OrchestrationToolProvider.CreateTools(
            sp.GetRequiredService<OrchestrationRunner>(),
            sp.GetRequiredService<OrchestrationStore>()));

        return services;
    }
}

/// <summary>
/// On start, marks runs a restart cut short so they can be resumed, then removes old
/// finished runs once a day.
/// </summary>
public sealed class OrchestrationMaintenanceService(
    OrchestrationStore store,
    OrchestrationRunner runner,
    ILogger<OrchestrationMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var interrupted = store.MarkInterrupted();
            if (interrupted > 0)
                logger.LogWarning("{Count} orchestration run(s) were cut short by a restart. Resume them from /api/orchestrations", interrupted);

            using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
            do
            {
                var removed = runner.PruneOld();
                if (removed > 0)
                    logger.LogInformation("Removed {Count} old orchestration run(s)", removed);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
    }
}
