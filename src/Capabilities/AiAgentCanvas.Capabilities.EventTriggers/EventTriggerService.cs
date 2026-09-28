using System.Threading.Channels;
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.EventTriggers;

public sealed class EventTriggerService : BackgroundService
{
    private readonly TriggerRegistry _registry;
    private readonly Channel<TriggerEvent> _eventChannel;
    private readonly EventTriggerOptions _options;
    private readonly ILogger<EventTriggerService> _logger;
    private readonly List<FileSystemWatcher> _watchers = [];

    public EventTriggerService(
        TriggerRegistry registry,
        Channel<TriggerEvent> eventChannel,
        EventTriggerOptions options,
        ILogger<EventTriggerService> logger)
    {
        _registry = registry;
        _eventChannel = eventChannel;
        _options = options;
        _logger = logger;
    }

    public ChannelReader<TriggerEvent> Events => _eventChannel.Reader;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EventTriggerService started");

        SetupFileWatchers();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckScheduledTriggers(stoppingToken);
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

    private async Task CheckScheduledTriggers(CancellationToken ct)
    {
        var scheduledTriggers = _registry.GetEnabled(EventTriggerType.Scheduled);
        foreach (var trigger in scheduledTriggers)
        {
            if (ShouldFire(trigger))
            {
                await FireTrigger(trigger, ct);
            }
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

    private async Task FireTrigger(EventTrigger trigger, CancellationToken ct)
    {
        var evt = new TriggerEvent
        {
            TriggerId = trigger.Id,
            TriggerName = trigger.Name,
            Message = trigger.AgentMessage,
            TargetAgent = trigger.TargetAgent,
        };

        _registry.RecordFired(trigger.Id);
        await _eventChannel.Writer.WriteAsync(evt, ct);
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
            Metadata = { ["filePath"] = e.FullPath, ["changeType"] = e.ChangeType.ToString() },
        };

        _registry.RecordFired(trigger.Id);

        // The queue is bounded, so a file storm drops events rather than growing
        // without limit. The drop is logged so it is not silent.
        if (_eventChannel.Writer.TryWrite(evt))
            _logger.LogInformation("File trigger {Id} fired: {File} ({Change})", trigger.Id, e.Name, e.ChangeType);
        else
            _logger.LogWarning("Trigger queue full, dropped file event for trigger {Id}: {File}", trigger.Id, e.Name);
    }

    private void CleanupWatchers()
    {
        foreach (var watcher in _watchers)
            watcher.Dispose();
        _watchers.Clear();
    }
}
