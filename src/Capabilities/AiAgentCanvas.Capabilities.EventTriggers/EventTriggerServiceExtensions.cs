using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.Capabilities.EventTriggers;

public static class EventTriggerServiceExtensions
{
    public static IServiceCollection AddAiAgentCanvasEventTriggers(this IServiceCollection services)
    {
        services.AddSingleton<TriggerRegistry>();
        services.AddSingleton(Channel.CreateUnbounded<TriggerEvent>());
        services.AddHostedService<EventTriggerService>();

        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            EventTriggerToolProvider.CreateTools(sp.GetRequiredService<TriggerRegistry>()));

        return services;
    }
}
