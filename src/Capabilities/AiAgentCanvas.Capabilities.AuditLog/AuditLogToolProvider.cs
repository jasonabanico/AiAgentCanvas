#pragma warning disable MEAI001

using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Capabilities.AuditLog;

public static class AuditLogToolProvider
{
    public static IReadOnlyList<AITool> CreateTools(AuditLogStore store)
    {
        return
        [
            AIFunctionFactory.Create(
                [Description("Query the audit log for recent actions taken by agents")]
                (string? eventType, string? agentName, string? toolName, int? limit) =>
                {
                    var results = store.Query(eventType, agentName, toolName, limit: limit ?? 10);
                    return JsonSerializer.Serialize(results.Select(e => new
                    {
                        e.Id, e.EventType, e.AgentName, e.ToolName,
                        e.ResultStatus, e.Details, e.TokensUsed,
                        e.DurationMs, Timestamp = e.Timestamp.ToString("g"),
                    }));
                }, "query_audit_log"),

            AIFunctionFactory.Create(
                [Description("Get summary statistics from the audit log")]
                (int? hoursBack) =>
                {
                    var since = hoursBack.HasValue
                        ? DateTimeOffset.UtcNow.AddHours(-hoursBack.Value)
                        : (DateTimeOffset?)null;
                    var stats = store.GetStats(since);
                    return JsonSerializer.Serialize(new
                    {
                        stats.TotalEvents, stats.ToolCalls, stats.Errors, stats.Handoffs,
                        Period = hoursBack.HasValue ? $"last {hoursBack}h" : "all time",
                    });
                }, "audit_stats"),
        ];
    }
}
