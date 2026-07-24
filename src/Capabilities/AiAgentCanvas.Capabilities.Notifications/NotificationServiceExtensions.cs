using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.Capabilities.Notifications;

public static class NotificationServiceExtensions
{
    public static IServiceCollection AddAiAgentCanvasNotifications(this IServiceCollection services)
    {
        var sink = new InMemoryNotificationSink();
        services.AddSingleton<INotificationSink>(sink);
        services.AddSingleton(sink);
        return services;
    }
}
