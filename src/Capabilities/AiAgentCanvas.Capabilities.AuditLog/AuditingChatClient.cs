#pragma warning disable MEAI001

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.AuditLog;

public sealed class AuditingChatClient : DelegatingChatClient
{
    private readonly AuditLogStore _store;
    private readonly string _agentName;
    private readonly ILogger? _logger;

    public AuditingChatClient(
        IChatClient inner,
        AuditLogStore store,
        string agentName = "AiAgentCanvas",
        ILogger? logger = null) : base(inner)
    {
        _store = store;
        _agentName = agentName;
        _logger = logger;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var toolCount = options?.Tools?.Count ?? 0;

        try
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken);
            sw.Stop();

            _store.Record(new AuditEntry
            {
                EventType = AuditEventTypes.ModelInvocation,
                AgentName = _agentName,
                ResultStatus = "success",
                TokensUsed = (int)((response.Usage?.InputTokenCount ?? 0) + (response.Usage?.OutputTokenCount ?? 0)),
                DurationMs = sw.ElapsedMilliseconds,
                Details = $"tools_available={toolCount}",
            });

            foreach (var msg in response.Messages)
            {
                foreach (var call in msg.Contents.OfType<FunctionCallContent>())
                {
                    _store.Record(new AuditEntry
                    {
                        EventType = AuditEventTypes.ToolCall,
                        AgentName = _agentName,
                        ToolName = call.Name,
                        Parameters = call.Arguments is not null
                            ? JsonSerializer.Serialize(call.Arguments)
                            : null,
                        ResultStatus = "invoked",
                        DurationMs = 0,
                    });
                }

                foreach (var result in msg.Contents.OfType<FunctionResultContent>())
                {
                    _store.Record(new AuditEntry
                    {
                        EventType = AuditEventTypes.ToolResult,
                        AgentName = _agentName,
                        ToolName = result.CallId,
                        ResultStatus = result.Result is string s && s.Contains("error", StringComparison.OrdinalIgnoreCase)
                            ? "error" : "success",
                        DurationMs = 0,
                    });
                }
            }

            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _store.Record(new AuditEntry
            {
                EventType = AuditEventTypes.Error,
                AgentName = _agentName,
                ResultStatus = "error",
                Details = ex.Message,
                DurationMs = sw.ElapsedMilliseconds,
            });
            throw;
        }
    }
}
