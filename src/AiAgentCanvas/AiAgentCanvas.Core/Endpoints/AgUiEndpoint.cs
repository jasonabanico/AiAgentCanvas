using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Core.Endpoints;

public static class AgUiEndpoint
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void MapAgUiEndpoints(this WebApplication app)
    {
        app.MapPost("/api/copilotkit", HandleCopilotKitRequest);
    }

    private static async Task HandleCopilotKitRequest(
        HttpContext context,
        IServiceProvider sp,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("AiAgentCanvas.Core.Endpoints.AgUiEndpoint");

        AIAgent agent;
        try
        {
            agent = sp.GetRequiredService<AIAgent>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve AIAgent");
            context.Response.StatusCode = 503;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "AI service is not configured. Update the AIFoundry section in appsettings.json with valid credentials.",
                details = ex.InnerException?.Message ?? ex.Message
            });
            return;
        }

        JsonElement body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse request body");
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid request body" });
            return;
        }

        var messages = body.GetProperty("messages");
        var threadId = body.TryGetProperty("threadId", out var tid) ? tid.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();
        var runId = Guid.NewGuid().ToString();

        logger.LogInformation("AG-UI request received. ThreadId={ThreadId}, MessageCount={Count}", threadId, messages.GetArrayLength());

        var chatMessages = ExtractMessages(messages);

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        await WriteEvent(context, "run.started", new { threadId, runId });

        var messageId = Guid.NewGuid().ToString();
        await WriteEvent(context, "text.message.start", new
        {
            messageId,
            role = "assistant",
            agentName = agent.Name,
        });

        var idleTimeout = TimeSpan.FromSeconds(120);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            cts.CancelAfter(idleTimeout);

            var session = await agent.CreateSessionAsync(cts.Token);
            session.StateBag.SetValue("conversationId", threadId);

            await foreach (var update in agent.RunStreamingAsync(chatMessages, session, cancellationToken: cts.Token))
            {
                cts.CancelAfter(idleTimeout);
                if (update.Text is { Length: > 0 } text)
                {
                    await WriteEvent(context, "text.message.content", new { messageId, delta = text });
                }
            }
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning("Agent execution idle timeout after {Seconds}s. ThreadId={ThreadId}", idleTimeout.TotalSeconds, threadId);
            await WriteEvent(context, "text.message.content", new
            {
                messageId,
                delta = $"\n\n**Error:** No progress for {idleTimeout.TotalSeconds} seconds. The agent may be stuck or the AI service is not responding."
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during agent execution. ThreadId={ThreadId}", threadId);
            await WriteEvent(context, "text.message.content", new
            {
                messageId,
                delta = $"\n\n**Error:** {ex.GetType().Name}: {ex.Message}"
            });
        }

        await WriteEvent(context, "text.message.end", new { messageId });
        await WriteEvent(context, "run.finished", new { threadId, runId });
    }

    private static List<ChatMessage> ExtractMessages(JsonElement messages)
    {
        var chatMessages = new List<ChatMessage>();
        for (var i = 0; i < messages.GetArrayLength(); i++)
        {
            var msg = messages[i];
            if (msg.TryGetProperty("role", out var role) &&
                msg.TryGetProperty("content", out var content) &&
                role.GetString() is { } r &&
                content.GetString() is { } c)
            {
                var chatRole = r switch
                {
                    "assistant" => ChatRole.Assistant,
                    "system" => ChatRole.System,
                    _ => ChatRole.User,
                };
                chatMessages.Add(new ChatMessage(chatRole, c));
            }
        }
        return chatMessages;
    }

    private static async Task WriteEvent(HttpContext context, string eventType, object data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await context.Response.WriteAsync($"event: {eventType}\ndata: {json}\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }
}
