using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.EventTriggers;

public sealed class TriggerRegistry
{
    private readonly ConcurrentDictionary<string, EventTrigger> _triggers = new();
    private readonly ILogger<TriggerRegistry> _logger;

    public TriggerRegistry(ILogger<TriggerRegistry> logger) => _logger = logger;

    public void Register(EventTrigger trigger)
    {
        _triggers[trigger.Id] = trigger;
        _logger.LogInformation("Registered trigger {Id}: {Name} ({Type})", trigger.Id, trigger.Name, trigger.Type);
    }

    public bool Remove(string id)
    {
        var removed = _triggers.TryRemove(id, out _);
        if (removed)
            _logger.LogInformation("Removed trigger {Id}", id);
        return removed;
    }

    public EventTrigger? Get(string id) => _triggers.GetValueOrDefault(id);

    public IReadOnlyList<EventTrigger> ListAll() => _triggers.Values.ToList();

    public IReadOnlyList<EventTrigger> GetEnabled(EventTriggerType type) =>
        _triggers.Values.Where(t => t.Enabled && t.Type == type).ToList();

    public void RecordFired(string id)
    {
        if (_triggers.TryGetValue(id, out var trigger))
        {
            trigger.LastFired = DateTimeOffset.UtcNow;
            trigger.FireCount++;
        }
    }
}
