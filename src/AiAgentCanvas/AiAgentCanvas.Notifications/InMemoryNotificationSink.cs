using System.Threading.Channels;
using AiAgentCanvas.Abstractions;

namespace AiAgentCanvas.Notifications;

public sealed class InMemoryNotificationSink : INotificationSink
{
    private readonly Channel<AgentNotification> _channel = Channel.CreateUnbounded<AgentNotification>(
        new UnboundedChannelOptions { SingleWriter = false, SingleReader = false });

    public async Task SendAsync(AgentNotification notification, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync(notification, ct);
    }

    public async IAsyncEnumerable<AgentNotification> SubscribeAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var notification in _channel.Reader.ReadAllAsync(ct))
        {
            yield return notification;
        }
    }
}
