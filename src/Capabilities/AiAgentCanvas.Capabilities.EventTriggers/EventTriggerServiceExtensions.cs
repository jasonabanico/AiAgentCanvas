using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.Capabilities.EventTriggers;

public static class EventTriggerServiceExtensions
{
    public static IServiceCollection AddAiAgentCanvasEventTriggers(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var options = new EventTriggerOptions();
        configuration?.GetSection(EventTriggerOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        services.AddSingleton(_ =>
        {
            var path = Path.IsPathRooted(options.DatabasePath)
                ? options.DatabasePath
                : Path.Combine(Directory.GetCurrentDirectory(), options.DatabasePath);
            return new TriggerStore(path);
        });

        // The same store holds cursors, so pull-mode readers share one durable position.
        services.AddSingleton<ICursorStore>(sp => sp.GetRequiredService<TriggerStore>());

        services.AddSingleton<TriggerRegistry>();

        // Available to the job runner when the Jobs capability is on. Harmless otherwise.
        services.AddSingleton<IAgentJob, TriggerQueueHealthJob>();
        services.AddSingleton<TriggerEventQueue>();

        // Connected services publish their events here when the Connectors capability is on.
        services.AddSingleton<IConnectorEventSink, ConnectorEventBridge>();

        services.AddHostedService<EventTriggerService>();

        // The producer alone is not a feature: this is what consumes fired events.
        services.AddHostedService<TriggerDispatchService>();

        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            EventTriggerToolProvider.CreateTools(
                sp.GetRequiredService<TriggerRegistry>(),
                sp.GetRequiredService<TriggerEventQueue>()));

        return services;
    }
}
