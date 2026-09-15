using System.Threading.Channels;
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

        services.AddSingleton<TriggerRegistry>();

        services.AddSingleton(Channel.CreateBounded<TriggerEvent>(
            new BoundedChannelOptions(Math.Max(16, options.QueueCapacity))
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
            }));

        services.AddHostedService<EventTriggerService>();

        // The producer alone is not a feature: this is what consumes fired events.
        services.AddHostedService<TriggerDispatchService>();

        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            EventTriggerToolProvider.CreateTools(sp.GetRequiredService<TriggerRegistry>()));

        return services;
    }
}
