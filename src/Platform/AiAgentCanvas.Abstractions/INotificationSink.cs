namespace AiAgentCanvas.Abstractions;

public sealed class AgentNotification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("o");
}

public interface INotificationSink
{
    Task SendAsync(AgentNotification notification, CancellationToken ct = default);
    IAsyncEnumerable<AgentNotification> SubscribeAsync(CancellationToken ct = default);
}
