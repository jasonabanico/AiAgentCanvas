using System.Text.Json;
using AiAgentCanvas.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AiAgentCanvas.Capabilities.Notifications;

public static class NotificationEndpoint
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void MapNotificationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/notifications", HandleNotificationStream);
    }

    private static async Task HandleNotificationStream(HttpContext context, INotificationSink sink)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        await foreach (var notification in sink.SubscribeAsync(context.RequestAborted))
        {
            var json = JsonSerializer.Serialize(notification, JsonOptions);
            await context.Response.WriteAsync($"event: notification\ndata: {json}\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    }
}
