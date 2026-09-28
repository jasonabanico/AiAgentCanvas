using System.Diagnostics;
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.Scheduling;

public sealed class SchedulerOptions
{
    public const string SectionName = "Agent:Scheduler";

    /// <summary>How often the runner checks for due tasks. Cron resolution is one minute.</summary>
    public int TickSeconds { get; set; } = 30;

    /// <summary>Tasks executed concurrently per tick. Beyond this, the rest wait for the next tick.</summary>
    public int MaxConcurrentTasks { get; set; } = 2;

    /// <summary>Wall-clock ceiling on a single task run.</summary>
    public int TaskTimeoutMinutes { get; set; } = 15;
}

/// <summary>
/// Executes scheduled tasks when they come due. Without this the scheduler tools
/// write rows that nothing ever reads: a task is accepted, stored, and silently
/// never runs.
/// </summary>
public sealed class ScheduledTaskRunner : BackgroundService
{
    private readonly IScheduledTaskStore _store;
    private readonly ScheduledAgentJob _job;
    private readonly SchedulerOptions _options;
    private readonly ILogger<ScheduledTaskRunner> _logger;

    public ScheduledTaskRunner(
        IScheduledTaskStore store,
        ScheduledAgentJob job,
        SchedulerOptions options,
        ILogger<ScheduledTaskRunner> logger)
    {
        _store = store;
        _job = job;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Scheduled task runner started, checking every {Seconds}s", _options.TickSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _options.TickSeconds)));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDueTasksAsync(stoppingToken);
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled task tick failed");
            }
        }
    }

    private async Task RunDueTasksAsync(CancellationToken stoppingToken)
    {
        var now = DateTimeOffset.UtcNow;
        var due = _store.ListTasks().Where(t => IsDue(t, now)).Take(_options.MaxConcurrentTasks).ToList();

        if (due.Count == 0)
            return;

        _logger.LogInformation("Running {Count} due scheduled task(s)", due.Count);
        await Task.WhenAll(due.Select(task => RunOneAsync(task, now, stoppingToken)));
    }

    private async Task RunOneAsync(ScheduledTaskRecord task, DateTimeOffset now, CancellationToken stoppingToken)
    {
        using var activity = AgentTelemetry.Source.StartActivity("scheduled task", ActivityKind.Internal);
        activity?.SetTag("task.id", task.Id);
        activity?.SetTag("task.recurring", task.IsRecurring);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, _options.TaskTimeoutMinutes)));

        // Claim the task before running it, so a long run is not picked up twice by
        // the next tick and a crash mid-run does not replay on restart.
        _store.MarkRun(task.Id, now);

        try
        {
            await _job.ExecuteAsync(task.Id, task.Description, task.Prompt, timeout.Token);
            activity?.SetStatus(ActivityStatusCode.Ok);

            if (!task.IsRecurring)
            {
                _store.RemoveTask(task.Id);
                _logger.LogInformation("One-shot task {TaskId} completed and removed", task.Id);
            }
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            var message = $"timed out after {_options.TaskTimeoutMinutes} minutes";
            _store.MarkRun(task.Id, now, message);
            activity?.SetStatus(ActivityStatusCode.Error, message);
            _logger.LogWarning("Scheduled task {TaskId} {Message}", task.Id, message);
        }
        catch (Exception ex)
        {
            _store.MarkRun(task.Id, now, ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Scheduled task {TaskId} failed", task.Id);
        }
    }

    /// <summary>
    /// A recurring task is due when its cron expression has a matching minute since
    /// the last run. A one-shot task is due once, the first time it is seen.
    /// </summary>
    private bool IsDue(ScheduledTaskRecord task, DateTimeOffset now)
    {
        if (!task.IsRecurring || string.IsNullOrWhiteSpace(task.CronExpression))
            return task.LastRunAt is null;

        if (!CronSchedule.TryParse(task.CronExpression, out var schedule) || schedule is null)
        {
            _logger.LogWarning(
                "Task {TaskId} has an unparsable cron expression '{Cron}' and will not run",
                task.Id, task.CronExpression);
            return false;
        }

        return schedule.IsDue(task.LastRunAt, now);
    }
}
