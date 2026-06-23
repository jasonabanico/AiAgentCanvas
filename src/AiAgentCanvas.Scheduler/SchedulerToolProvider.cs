using System.ComponentModel;
using System.Text.Json;
using Hangfire;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Scheduler;

public sealed class SchedulerToolProvider
{
    private readonly ScheduledTaskStore _store;
    private readonly AutonomousExecutionOptions _autonomousOptions;
    private readonly IRecurringJobManager _recurringJobManager;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public SchedulerToolProvider(
        ScheduledTaskStore store,
        AutonomousExecutionOptions autonomousOptions,
        IRecurringJobManager recurringJobManager,
        IBackgroundJobClient backgroundJobClient)
    {
        _store = store;
        _autonomousOptions = autonomousOptions;
        _recurringJobManager = recurringJobManager;
        _backgroundJobClient = backgroundJobClient;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(ScheduleRecurringTask, "schedule_recurring_task",
                "Schedule a recurring task that runs the AI agent with a given prompt on a cron schedule"),
            AIFunctionFactory.Create(ScheduleOneTimeTask, "schedule_one_time_task",
                "Schedule a one-time delayed task that runs the AI agent with a given prompt after a delay"),
            AIFunctionFactory.Create(ListScheduledTasks, "list_scheduled_tasks",
                "List all scheduled tasks"),
            AIFunctionFactory.Create(RemoveScheduledTask, "remove_scheduled_task",
                "Remove a scheduled task by ID"),
            AIFunctionFactory.Create(GetTaskResults, "get_task_results",
                "Get recent results from completed scheduled tasks"),
            AIFunctionFactory.Create(StartAutonomousMode, "start_autonomous_mode",
                "Enable autonomous execution mode -- the agent will poll for work items and goals and execute them independently"),
            AIFunctionFactory.Create(StopAutonomousMode, "stop_autonomous_mode",
                "Disable autonomous execution mode"),
            AIFunctionFactory.Create(GetAutonomousStatus, "get_autonomous_status",
                "Check whether autonomous execution mode is currently enabled"),
        ];
    }

    [Description("Schedule a recurring task that runs the AI agent with a given prompt on a cron schedule")]
    private string ScheduleRecurringTask(
        [Description("Short description of what the task does")] string description,
        [Description("The prompt to send to the AI agent when the task runs")] string prompt,
        [Description("Cron expression for the schedule (e.g. '0 8 * * *' for daily at 8am, '0 */2 * * *' for every 2 hours)")] string cronExpression)
    {
        var taskId = $"task-{Guid.NewGuid():N}"[..16];

        _store.SaveTask(new ScheduledTaskRecord
        {
            Id = taskId,
            Description = description,
            Prompt = prompt,
            CronExpression = cronExpression,
            IsRecurring = true,
        });

        _recurringJobManager.AddOrUpdate<ScheduledAgentJob>(
            taskId,
            job => job.ExecuteAsync(taskId, description, prompt),
            cronExpression);

        return JsonSerializer.Serialize(new { status = "scheduled", taskId, description, cronExpression });
    }

    [Description("Schedule a one-time delayed task that runs the AI agent with a given prompt after a delay")]
    private string ScheduleOneTimeTask(
        [Description("Short description of what the task does")] string description,
        [Description("The prompt to send to the AI agent when the task runs")] string prompt,
        [Description("Delay in minutes before the task runs")] int delayMinutes)
    {
        var taskId = $"task-{Guid.NewGuid():N}"[..16];

        _store.SaveTask(new ScheduledTaskRecord
        {
            Id = taskId,
            Description = description,
            Prompt = prompt,
            IsRecurring = false,
        });

        _backgroundJobClient.Schedule<ScheduledAgentJob>(
            job => job.ExecuteAsync(taskId, description, prompt),
            TimeSpan.FromMinutes(delayMinutes));

        return JsonSerializer.Serialize(new { status = "scheduled", taskId, description, runsIn = $"{delayMinutes} minutes" });
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
        _recurringJobManager.RemoveIfExists(taskId);

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

    [Description("Enable autonomous execution mode -- the agent will poll for work items and goals and execute them independently")]
    private string StartAutonomousMode()
    {
        _autonomousOptions.Enabled = true;
        _recurringJobManager.AddOrUpdate<AutonomousAgentJob>(
            "autonomous-execution",
            job => job.ExecuteAsync(),
            _autonomousOptions.CronExpression);

        return JsonSerializer.Serialize(new
        {
            status = "enabled",
            pollInterval = _autonomousOptions.CronExpression,
            maxIterationsPerRun = _autonomousOptions.MaxIterationsPerRun,
        });
    }

    [Description("Disable autonomous execution mode")]
    private string StopAutonomousMode()
    {
        _autonomousOptions.Enabled = false;
        _recurringJobManager.RemoveIfExists("autonomous-execution");

        return JsonSerializer.Serialize(new { status = "disabled" });
    }

    [Description("Check whether autonomous execution mode is currently enabled")]
    private string GetAutonomousStatus()
    {
        return JsonSerializer.Serialize(new
        {
            enabled = _autonomousOptions.Enabled,
            maxIterationsPerRun = _autonomousOptions.MaxIterationsPerRun,
            pollInterval = _autonomousOptions.CronExpression,
        });
    }
}
