using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.EventTriggers;

public sealed class EventTriggerService : BackgroundService
{
    private readonly TriggerRegistry _registry;
    private readonly TriggerEventQueue _queue;
    private readonly EventTriggerOptions _options;
    private readonly ILogger<EventTriggerService> _logger;
    private readonly List<FileSystemWatcher> _watchers = [];

    public EventTriggerService(
        TriggerRegistry registry,
        TriggerEventQueue queue,
        EventTriggerOptions options,
        ILogger<EventTriggerService> logger)
    {
        _registry = registry;
        _queue = queue;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EventTriggerService started");

        SetupFileWatchers();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                CheckScheduledTriggers();
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _options.PollSeconds)), stoppingToken);
                RefreshFileWatchers();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in trigger evaluation loop");
            }
        }

        CleanupWatchers();
    }

    private void CheckScheduledTriggers()
    {
        var scheduledTriggers = _registry.GetEnabled(EventTriggerType.Scheduled);
        foreach (var trigger in scheduledTriggers)
        {
            if (ShouldFire(trigger))
                FireTrigger(trigger);
        }
    }

    private bool ShouldFire(EventTrigger trigger)
    {
        if (string.IsNullOrWhiteSpace(trigger.CronExpression))
            return false;

        if (!CronSchedule.TryParse(trigger.CronExpression, out var schedule) || schedule is null)
        {
            _logger.LogWarning(
                "Trigger {Id} has an unparsable cron expression '{Cron}' and will not fire",
                trigger.Id, trigger.CronExpression);
            return false;
        }

        return schedule.IsDue(trigger.LastFired, DateTimeOffset.UtcNow);
    }

    private void FireTrigger(EventTrigger trigger)
    {
        var now = DateTimeOffset.UtcNow;
        var evt = new TriggerEvent
        {
            TriggerId = trigger.Id,
            TriggerName = trigger.Name,
            Message = trigger.AgentMessage,
            TargetAgent = trigger.TargetAgent,
            TargetJob = trigger.TargetJob,
            // One event per trigger per minute, so a restart that races the last-fired
            // write cannot run the same occurrence twice.
            DedupeKey = $"scheduled:{now:yyyyMMddHHmm}",
        };

        // A refused event leaves the trigger due, so the next poll tries again.
        if (_queue.Enqueue(evt) == EnqueueOutcome.Rejected)
            return;

        _registry.RecordFired(trigger.Id);
        _logger.LogInformation("Fired trigger {Id}: {Name}", trigger.Id, trigger.Name);
    }

    private void SetupFileWatchers()
    {
        var fileWatchTriggers = _registry.GetEnabled(EventTriggerType.FileWatch);
        foreach (var trigger in fileWatchTriggers)
        {
            if (trigger.WatchPath is null) continue;
            AddWatcher(trigger);
        }
    }

    private void RefreshFileWatchers()
    {
        var currentTriggers = _registry.GetEnabled(EventTriggerType.FileWatch);
        var watchedPaths = _watchers.Select(w => w.Path).ToHashSet();

        foreach (var trigger in currentTriggers)
        {
            if (trigger.WatchPath is not null && !watchedPaths.Contains(trigger.WatchPath))
                AddWatcher(trigger);
        }
    }

    private void AddWatcher(EventTrigger trigger)
    {
        if (trigger.WatchPath is null || !Directory.Exists(trigger.WatchPath))
            return;

        var watcher = new FileSystemWatcher(trigger.WatchPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
        };

        watcher.Changed += (_, e) => OnFileChanged(trigger, e);
        watcher.Created += (_, e) => OnFileChanged(trigger, e);

        _watchers.Add(watcher);
        _logger.LogInformation("File watcher added for trigger {Id}: {Path}", trigger.Id, trigger.WatchPath);
    }

    private void OnFileChanged(EventTrigger trigger, FileSystemEventArgs e)
    {
        var evt = new TriggerEvent
        {
            TriggerId = trigger.Id,
            TriggerName = trigger.Name,
            Message = $"{trigger.AgentMessage} [File: {e.Name}, Change: {e.ChangeType}]",
            TargetAgent = trigger.TargetAgent,
            TargetJob = trigger.TargetJob,
            Metadata = { ["filePath"] = e.FullPath, ["changeType"] = e.ChangeType.ToString() },
            // One write raises several change events within a moment. Coalescing them
            // by second keeps a single save from running the agent more than once.
            DedupeKey = $"file:{e.FullPath}:{e.ChangeType}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
        };

        switch (_queue.Enqueue(evt))
        {
            case EnqueueOutcome.Accepted:
                _registry.RecordFired(trigger.Id);
                _logger.LogInformation("File trigger {Id} fired: {File} ({Change})", trigger.Id, e.Name, e.ChangeType);
                break;
            case EnqueueOutcome.Rejected:
                _logger.LogWarning("Trigger backlog full, refused file event for trigger {Id}: {File}", trigger.Id, e.Name);
                break;
        }
    }

    private void CleanupWatchers()
    {
        foreach (var watcher in _watchers)
            watcher.Dispose();
        _watchers.Clear();
    }
}
