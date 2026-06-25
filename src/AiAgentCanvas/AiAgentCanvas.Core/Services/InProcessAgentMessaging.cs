using System.Collections.Concurrent;
using AiAgentCanvas.Abstractions;

namespace AiAgentCanvas.Core.Services;

public sealed class InProcessAgentMessaging : IAgentMessaging
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<AgentMessage>> _mailboxes = new(StringComparer.OrdinalIgnoreCase);

    public Task SendAsync(AgentMessage message, CancellationToken cancellationToken = default)
    {
        var queue = _mailboxes.GetOrAdd(message.ToAgent, _ => new ConcurrentQueue<AgentMessage>());
        queue.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task<AgentMessage?> ReceiveAsync(string agentName, CancellationToken cancellationToken = default)
    {
        if (_mailboxes.TryGetValue(agentName, out var queue) && queue.TryDequeue(out var message))
            return Task.FromResult<AgentMessage?>(message);

        return Task.FromResult<AgentMessage?>(null);
    }

    public Task<IReadOnlyList<AgentMessage>> ReceiveAllAsync(string agentName, CancellationToken cancellationToken = default)
    {
        var results = new List<AgentMessage>();
        if (_mailboxes.TryGetValue(agentName, out var queue))
        {
            while (queue.TryDequeue(out var message))
                results.Add(message);
        }
        return Task.FromResult<IReadOnlyList<AgentMessage>>(results);
    }
}
