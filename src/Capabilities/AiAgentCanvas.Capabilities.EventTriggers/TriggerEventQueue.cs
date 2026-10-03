using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.EventTriggers;

/// <summary>
/// The durable queue between whatever fires a trigger and the dispatcher that runs
/// the agent. It replaces an in-memory channel that lost every pending event on
/// restart and dropped events silently when full.
/// </summary>
/// <remarks>
/// Delivery is at least once. An event interrupted mid-run is queued again, so the
/// work an agent does for an event must be safe to repeat, which is what idempotency
/// keys on send tools are for.
/// </remarks>
public sealed class TriggerEventQueue
{
    private readonly TriggerStore _store;
    private readonly EventTriggerOptions _options;
    private readonly ILogger<TriggerEventQueue> _logger;
    private readonly SemaphoreSlim _signal = new(0, 1);

    public TriggerEventQueue(TriggerStore store, EventTriggerOptions options, ILogger<TriggerEventQueue> logger)
    {
        _store = store;
        _options = options;
        _logger = logger;
    }

    public EnqueueOutcome Enqueue(TriggerEvent evt)
    {
        var outcome = _store.Enqueue(evt, Math.Max(16, _options.QueueCapacity));
        Count(outcome.ToString().ToLowerInvariant());

        switch (outcome)
        {
            case EnqueueOutcome.Accepted:
                Wake();
                break;
            case EnqueueOutcome.Rejected:
                _logger.LogWarning(
                    "Trigger backlog is at capacity ({Capacity}), refused event for trigger {TriggerId}",
                    _options.QueueCapacity, evt.TriggerId);
                break;
            case EnqueueOutcome.Duplicate:
                _logger.LogDebug("Duplicate event for trigger {TriggerId} ({Key}), ignored", evt.TriggerId, evt.DedupeKey);
                break;
        }

        return outcome;
    }

    public TriggerEvent? ClaimNext(DateTimeOffset now) => _store.ClaimNext(now);

    /// <summary>Waits until an event is enqueued or the interval passes, whichever is first.</summary>
    public async Task WaitForWorkAsync(TimeSpan interval, CancellationToken ct)
    {
        await _signal.WaitAsync(interval, ct).ConfigureAwait(false);
    }

    public void Complete(TriggerEvent evt)
    {
        _store.Complete(evt.Id);
        Count("succeeded");
    }

    /// <summary>
    /// Schedules another attempt with exponential backoff, or parks the event as dead
    /// once the attempts are spent. Returns true when the event died.
    /// </summary>
    public bool Fail(TriggerEvent evt, string error, DateTimeOffset now)
    {
        if (evt.Attempts >= Math.Max(1, _options.MaxAttempts))
        {
            _store.MarkDead(evt.Id, error);
            Count("dead");
            _logger.LogError(
                "Trigger event {EventId} ({Trigger}) is dead after {Attempts} attempts: {Error}",
                evt.Id, evt.TriggerName, evt.Attempts, error);
            return true;
        }

        var delay = RetryDelay(evt.Attempts);
        _store.Retry(evt.Id, error, now + delay);
        Count("retried");
        _logger.LogWarning(
            "Trigger event {EventId} ({Trigger}) failed attempt {Attempts}, retrying in {Delay}: {Error}",
            evt.Id, evt.TriggerName, evt.Attempts, delay, error);
        return false;
    }

    internal TimeSpan RetryDelay(int attempts)
    {
        var seconds = _options.RetryBaseSeconds * Math.Pow(2, Math.Max(0, attempts - 1));
        var capped = Math.Min(seconds, _options.MaxRetryDelayMinutes * 60d);
        return TimeSpan.FromSeconds(Math.Max(0, capped));
    }

    public void Release(TriggerEvent evt) => _store.Release(evt.Id);

    /// <summary>Holds the event until the given time without spending an attempt.</summary>
    public void Defer(TriggerEvent evt, DateTimeOffset until, string reason)
    {
        _store.Defer(evt.Id, until, reason);
        Count("deferred");
    }

    public int RecoverInterrupted() => _store.RecoverInterrupted();

    public TriggerQueueCounts Counts() => _store.Counts();

    public IReadOnlyList<TriggerEvent> Dead(int limit = 50) => _store.ListByStatus(TriggerEventStatus.Dead, limit);

    public bool Retry(string eventId)
    {
        var requeued = _store.Requeue(eventId);
        if (requeued)
            Wake();
        return requeued;
    }

    public int PruneSucceeded() =>
        _options.SucceededRetentionDays > 0
            ? _store.PruneSucceeded(DateTimeOffset.UtcNow.AddDays(-_options.SucceededRetentionDays))
            : 0;

    private void Wake()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { /* a wake-up is already pending */ }
    }

    private static void Count(string outcome) =>
        AgentTelemetry.TriggerEvents.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
}
