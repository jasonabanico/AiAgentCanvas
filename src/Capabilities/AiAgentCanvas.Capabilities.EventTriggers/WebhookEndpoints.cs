using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AiAgentCanvas.Capabilities.EventTriggers;

public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapEventTriggerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/triggers");

        group.MapPost("/webhook/{triggerId}", async (
            string triggerId,
            HttpContext context,
            TriggerRegistry registry,
            Channel<TriggerEvent> eventChannel) =>
        {
            var trigger = registry.Get(triggerId);
            if (trigger is null || trigger.Type != EventTriggerType.Webhook || !trigger.Enabled)
                return Results.NotFound(new { error = "Trigger not found or not a webhook" });

            string? body = null;
            if (context.Request.ContentLength > 0)
            {
                using var reader = new StreamReader(context.Request.Body);
                body = await reader.ReadToEndAsync();
            }

            var evt = new TriggerEvent
            {
                TriggerId = trigger.Id,
                TriggerName = trigger.Name,
                Message = $"{trigger.AgentMessage} [Webhook payload: {body ?? "empty"}]",
                TargetAgent = trigger.TargetAgent,
                Metadata = { ["source"] = "webhook" },
            };

            registry.RecordFired(triggerId);
            await eventChannel.Writer.WriteAsync(evt);

            return Results.Ok(new { fired = true, triggerId });
        });

        group.MapGet("/", (TriggerRegistry registry) =>
        {
            return Results.Ok(registry.ListAll().Select(t => new
            {
                t.Id, t.Name, Type = t.Type.ToString(), t.Enabled,
                t.AgentMessage, t.TargetAgent, t.FireCount,
                LastFired = t.LastFired?.ToString("g"),
            }));
        });

        return endpoints;
    }
}
