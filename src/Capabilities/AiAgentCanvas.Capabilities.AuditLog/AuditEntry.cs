namespace AiAgentCanvas.Capabilities.AuditLog;

public sealed class AuditEntry
{
    public long Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public string? ToolName { get; set; }
    public string? Parameters { get; set; }
    public string? ResultStatus { get; set; }
    public string? Details { get; set; }
    public int? TokensUsed { get; set; }
    public long DurationMs { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public static class AuditEventTypes
{
    public const string ToolCall = "tool_call";
    public const string ToolResult = "tool_result";
    public const string ModelInvocation = "model_invocation";
    public const string AgentHandoff = "agent_handoff";
    public const string ApprovalGranted = "approval_granted";
    public const string ApprovalDenied = "approval_denied";
    public const string Error = "error";
}
