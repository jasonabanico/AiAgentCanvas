using System.ComponentModel;
using AiAgentCanvas.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Capabilities.Scheduling;

public sealed class SchedulerToolProvider
{
    private readonly IScheduledTaskStore _store;

    public SchedulerToolProvider(IScheduledTaskStore store)
    {
        _store = store;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(ScheduleTask, "schedule_task",
                "Schedule a task that runs the AI agent with a given prompt"),
            AIFunctionFactory.Create(ListScheduledTasks, "list_scheduled_tasks",
                "List all scheduled tasks"),
            AIFunctionFactory.Create(RemoveScheduledTask, "remove_scheduled_task",
                "Remove a scheduled task by ID"),
            AIFunctionFactory.Create(GetTaskResults, "get_task_results",
                "Get recent results from completed scheduled tasks"),
        ];
    }

    [Description("Schedule a task that runs the AI agent with a given prompt")]
    private string ScheduleTask(
        [Description("Short description of what the task does")] string description,
        [Description("The prompt to send to the AI agent when the task runs")] string prompt,
        [Description("5-field UTC cron expression for a recurring schedule, e.g. '0 8 * * *' for daily at 08:00. Omit for a task that runs once.")] string? cronExpression = null)
    {
        CronSchedule? schedule = null;
        if (!string.IsNullOrWhiteSpace(cronExpression)
            && !CronSchedule.TryParse(cronExpression, out schedule))
        {
            return JsonSerializer.Serialize(new
            {
                error = $"'{cronExpression}' is not a valid 5-field cron expression "
                    + "(minute hour day-of-month month day-of-week). Example: '0 8 * * *' for daily at 08:00 UTC.",
            });
        }

        var taskId = $"task-{Guid.NewGuid():N}"[..16];

        _store.SaveTask(new ScheduledTaskRecord
        {
            Id = taskId,
            Description = description,
            Prompt = prompt,
            CronExpression = cronExpression,
            IsRecurring = cronExpression is not null,
        });

        return JsonSerializer.Serialize(new
        {
            status = "scheduled",
            taskId,
            description,
            cronExpression,
            nextRunUtc = schedule?.GetNextOccurrence(DateTimeOffset.UtcNow)?.ToString("u"),
        });
    }

    [Description("List all scheduled tasks")]
    private string ListScheduledTasks()
    {
        var now = DateTimeOffset.UtcNow;
        var tasks = _store.ListTasks().Select(t => new
        {
            t.Id,
            t.Description,
            t.CronExpression,
            t.IsRecurring,
            t.CreatedAt,
            LastRunUtc = t.LastRunAt?.ToString("u"),
            NextRunUtc = NextRun(t, now)?.ToString("u"),
            t.LastError,
        }).ToList();

        return JsonSerializer.Serialize(new { count = tasks.Count, tasks }, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Remove a scheduled task by ID")]
    private string RemoveScheduledTask(
        [Description("The task ID to remove")] string taskId)
    {
        var removed = _store.RemoveTask(taskId);
        return removed
            ? JsonSerializer.Serialize(new { status = "removed", taskId })
            : JsonSerializer.Serialize(new { status = "not_found", taskId });
    }

    private static DateTimeOffset? NextRun(ScheduledTaskRecord task, DateTimeOffset now)
    {
        if (!task.IsRecurring || string.IsNullOrWhiteSpace(task.CronExpression))
            return task.LastRunAt is null ? now : null;

        return CronSchedule.TryParse(task.CronExpression, out var schedule) && schedule is not null
            ? schedule.GetNextOccurrence(task.LastRunAt ?? now)
            : null;
    }

    [Description("Get recent results from completed scheduled tasks")]
    private string GetTaskResults(
        [Description("Maximum number of results to return (default: 10)")] int limit = 10)
    {
        var results = _store.GetResults(limit);
        return JsonSerializer.Serialize(new { count = results.Count, results }, new JsonSerializerOptions { WriteIndented = true });
    }
}
