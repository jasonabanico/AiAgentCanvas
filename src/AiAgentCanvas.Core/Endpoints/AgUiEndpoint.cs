using System.Diagnostics;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
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
        AIAgent agent,
        ILoggerFactory loggerFactory)
    {
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);
        var messages = body.GetProperty("messages");
        var threadId = body.TryGetProperty("threadId", out var tid) ? tid.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();
        var runId = Guid.NewGuid().ToString();

        var logger = loggerFactory.CreateLogger("AiAgentCanvas.Core.Endpoints.AgUiEndpoint");
        logger.LogInformation("AG-UI request received. ThreadId={ThreadId}, MessageCount={Count}", threadId, messages.GetArrayLength());

        var chatMessages = ExtractMessages(messages);

        var session = await agent.CreateSessionAsync(context.RequestAborted);
        session.StateBag.SetValue("conversationId", threadId);

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

        var sw = Stopwatch.StartNew();
        await foreach (var update in agent.RunStreamingAsync(chatMessages, session, cancellationToken: context.RequestAborted))
        {
            if (update.Text is { Length: > 0 } text)
            {
                await WriteEvent(context, "text.message.content", new { messageId, delta = text });
            }
        }

        await WriteEvent(context, "text.message.end", new { messageId });
        await WriteEvent(context, "run.finished", new { threadId, runId });
        logger.LogInformation("Streaming complete. ThreadId={ThreadId}, RunId={RunId}, ElapsedMs={ElapsedMs}", threadId, runId, sw.ElapsedMilliseconds);
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
