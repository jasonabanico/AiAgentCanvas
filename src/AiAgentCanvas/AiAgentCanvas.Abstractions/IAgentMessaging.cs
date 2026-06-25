namespace AiAgentCanvas.Abstractions;

public sealed class AgentMessage
{
    public required string FromAgent { get; init; }
    public required string ToAgent { get; init; }
    public required string Content { get; init; }
    public string? CorrelationId { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public interface IAgentMessaging
{
    Task SendAsync(AgentMessage message, CancellationToken cancellationToken = default);
    Task<AgentMessage?> ReceiveAsync(string agentName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentMessage>> ReceiveAllAsync(string agentName, CancellationToken cancellationToken = default);
}
