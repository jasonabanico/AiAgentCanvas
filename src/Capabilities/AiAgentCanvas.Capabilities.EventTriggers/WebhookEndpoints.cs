using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AiAgentCanvas.Capabilities.EventTriggers;

public static class WebhookEndpoints
{
    public const string IdempotencyHeader = "Idempotency-Key";

    public static RouteGroupBuilder MapEventTriggerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/triggers");

        group.MapPost("/webhook/{triggerId}", async (
            string triggerId,
            HttpContext context,
            TriggerRegistry registry,
            TriggerEventQueue queue,
            EventTriggerOptions options) =>
        {
            var trigger = registry.Get(triggerId);
            if (trigger is null || trigger.Type != EventTriggerType.Webhook || !trigger.Enabled)
                return Results.NotFound(new { error = "Trigger not found or not a webhook" });

            var limit = Math.Max(1024, options.MaxWebhookBodyBytes);
            if (context.Request.ContentLength > limit)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            var body = await ReadBodyAsync(context.Request, limit);
            if (body is null)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            // A sender that retries sets a stable key, so the retry is recognised as the
            // same occurrence. Without a key every delivery is treated as new.
            var key = context.Request.Headers[IdempotencyHeader].FirstOrDefault();

            var evt = new TriggerEvent
            {
                TriggerId = trigger.Id,
                TriggerName = trigger.Name,
                Message = $"{trigger.AgentMessage} [Webhook payload: {(body.Length == 0 ? "empty" : body)}]",
                TargetAgent = trigger.TargetAgent,
                TargetJob = trigger.TargetJob,
                DedupeKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim(),
                Metadata = { ["source"] = "webhook" },
            };

            switch (queue.Enqueue(evt))
            {
                case EnqueueOutcome.Accepted:
                    registry.RecordFired(triggerId);
                    return Results.Accepted(value: new { fired = true, triggerId, eventId = evt.Id });

                case EnqueueOutcome.Duplicate:
                    return Results.Ok(new { fired = false, duplicate = true, triggerId });

                default:
                    context.Response.Headers.RetryAfter = "60";
                    return Results.Json(
                        new { fired = false, error = "The trigger backlog is full. Retry later." },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        group.MapGet("/", (TriggerRegistry registry) =>
        {
            return Results.Ok(registry.ListAll().Select(t => new
            {
                t.Id,
                t.Name,
                Type = t.Type.ToString(),
                t.Enabled,
                t.AgentMessage,
                t.TargetAgent,
                t.TargetJob,
                t.FireCount,
                LastFired = t.LastFired?.ToString("g"),
            }));
        });

        group.MapGet("/queue", (TriggerEventQueue queue) => Results.Ok(queue.Counts()));

        group.MapGet("/events/dead", (TriggerEventQueue queue, int? limit) =>
            Results.Ok(queue.Dead(limit ?? 50).Select(e => new
            {
                e.Id,
                e.TriggerId,
                e.TriggerName,
                e.Attempts,
                e.LastError,
                Created = e.FiredAt.ToString("o"),
            })));

        group.MapPost("/events/{eventId}/retry", (string eventId, TriggerEventQueue queue) =>
            queue.Retry(eventId)
                ? Results.Ok(new { requeued = true, eventId })
                : Results.NotFound(new { error = "No dead event with that id" }));

        return group;
    }

    /// <summary>Reads the body up to the limit. Returns null when it is larger.</summary>
    private static async Task<string?> ReadBodyAsync(HttpRequest request, int limit)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + read > limit)
                return null;
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
