using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.EventTriggers;

/// <summary>
/// The set of triggers. Backed by a <see cref="TriggerStore"/> when one is supplied, so
/// triggers an agent creates survive a restart. Without a store it is memory only,
/// which is how the unit tests use it.
/// </summary>
public sealed class TriggerRegistry
{
    private readonly ConcurrentDictionary<string, EventTrigger> _triggers = new();
    private readonly ILogger<TriggerRegistry> _logger;
    private readonly TriggerStore? _store;

    public TriggerRegistry(ILogger<TriggerRegistry> logger, TriggerStore? store = null)
    {
        _logger = logger;
        _store = store;

        if (_store is null)
            return;

        foreach (var trigger in _store.LoadTriggers())
            _triggers[trigger.Id] = trigger;

        if (!_triggers.IsEmpty)
            _logger.LogInformation("Loaded {Count} trigger(s) from storage", _triggers.Count);
    }

    public void Register(EventTrigger trigger)
    {
        _triggers[trigger.Id] = trigger;
        _store?.SaveTrigger(trigger);
        _logger.LogInformation("Registered trigger {Id}: {Name} ({Type})", trigger.Id, trigger.Name, trigger.Type);
    }

    public bool Remove(string id)
    {
        var removed = _triggers.TryRemove(id, out _);
        if (removed)
        {
            _store?.DeleteTrigger(id);
            _logger.LogInformation("Removed trigger {Id}", id);
        }
        return removed;
    }

    public bool SetEnabled(string id, bool enabled)
    {
        if (!_triggers.TryGetValue(id, out var trigger))
            return false;

        trigger.Enabled = enabled;
        _store?.SaveTrigger(trigger);
        return true;
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
            _store?.SaveTrigger(trigger);
        }
    }
}
