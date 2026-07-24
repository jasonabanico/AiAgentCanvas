using System.ComponentModel;
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
        [Description("Cron expression for recurring schedule (optional, e.g. '0 8 * * *' for daily at 8am)")] string? cronExpression = null)
    {
        var taskId = $"task-{Guid.NewGuid():N}"[..16];

        _store.SaveTask(new ScheduledTaskRecord
        {
            Id = taskId,
            Description = description,
            Prompt = prompt,
            CronExpression = cronExpression,
            IsRecurring = cronExpression is not null,
        });

        return JsonSerializer.Serialize(new { status = "scheduled", taskId, description, cronExpression });
    }

    [Description("List all scheduled tasks")]
    private string ListScheduledTasks()
    {
        var tasks = _store.ListTasks();
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

    [Description("Get recent results from completed scheduled tasks")]
    private string GetTaskResults(
        [Description("Maximum number of results to return (default: 10)")] int limit = 10)
    {
        var results = _store.GetResults(limit);
        return JsonSerializer.Serialize(new { count = results.Count, results }, new JsonSerializerOptions { WriteIndented = true });
    }
}
