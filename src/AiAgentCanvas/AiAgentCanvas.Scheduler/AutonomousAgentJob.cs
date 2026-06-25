using AiAgentCanvas.Abstractions;
using AiAgentCanvas.AgentData.Goals;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Scheduler;

public sealed class AutonomousAgentJob
{
    private readonly IServiceProvider _sp;
    private readonly GoalStore _goalStore;
    private readonly WorkQueueStore _workQueueStore;
    private readonly AutonomousExecutionOptions _options;
    private readonly INotificationSink? _notificationSink;
    private readonly ILogger<AutonomousAgentJob> _logger;

    public AutonomousAgentJob(
        IServiceProvider sp,
        GoalStore goalStore,
        WorkQueueStore workQueueStore,
        AutonomousExecutionOptions options,
        ILogger<AutonomousAgentJob> logger,
        INotificationSink? notificationSink = null)
    {
        _sp = sp;
        _goalStore = goalStore;
        _workQueueStore = workQueueStore;
        _options = options;
        _notificationSink = notificationSink;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Autonomous execution is disabled, skipping");
            return;
        }

        var iterations = 0;

        while (iterations < _options.MaxIterationsPerRun)
        {
            var workItem = _workQueueStore.ClaimNext();
            if (workItem is null)
            {
                var nextGoal = PickNextGoal();
                if (nextGoal is null)
                {
                    _logger.LogDebug("No pending work items or active goals, stopping");
                    break;
                }

                var itemId = _workQueueStore.Submit(
                    $"[Goal: {nextGoal.Name}] {nextGoal.Content}",
                    nextGoal.Priority,
                    nextGoal.AssignedAgent,
                    nextGoal.Name);

                workItem = _workQueueStore.ClaimNext();
                if (workItem is null) break;
            }

            iterations++;
            _logger.LogInformation(
                "Autonomous iteration {Iteration}/{Max}: executing work item {Id}",
                iterations, _options.MaxIterationsPerRun, workItem.Id);

            try
            {
                var result = await ExecuteWorkItem(workItem);
                _workQueueStore.Complete(workItem.Id, result);
                _logger.LogInformation("Work item {Id} completed successfully", workItem.Id);

                if (_notificationSink is not null)
                {
                    await _notificationSink.SendAsync(new AgentNotification
                    {
                        Title = $"Autonomous: {workItem.Description[..Math.Min(80, workItem.Description.Length)]}",
                        Body = result.Length > 500 ? result[..500] + "..." : result,
                        Source = $"autonomous:{workItem.Id}",
                    });
                }
            }
            catch (Exception ex)
            {
                _workQueueStore.Fail(workItem.Id, ex.Message);
                _logger.LogError(ex, "Work item {Id} failed", workItem.Id);
            }
        }

        if (iterations > 0)
            _logger.LogInformation("Autonomous run completed: {Iterations} items processed", iterations);
    }

    private GoalEntry? PickNextGoal()
    {
        var activeGoals = _goalStore.ListActive();
        if (activeGoals.Count == 0) return null;

        var pendingWorkGoals = _workQueueStore.List("pending")
            .Where(w => w.GoalName is not null)
            .Select(w => w.GoalName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return activeGoals.FirstOrDefault(g => !pendingWorkGoals.Contains(g.Name));
    }

    private async Task<string> ExecuteWorkItem(WorkItem workItem)
    {
        var agent = _sp.GetRequiredService<AIAgent>();
        var session = await agent.CreateSessionAsync();

        var prompt = BuildPrompt(workItem);
        var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };

        var response = await agent.RunAsync(messages, session);
        return response.Text ?? "(no response)";
    }

    private string BuildPrompt(WorkItem workItem)
    {
        var parts = new List<string>
        {
            "You are operating in autonomous mode. Execute the following work item and report the result.",
            "",
            $"## Work Item: {workItem.Id}",
            workItem.Description,
        };

        if (workItem.GoalName is not null)
        {
            var goal = _goalStore.Get(workItem.GoalName);
            if (goal is not null)
            {
                parts.Add("");
                parts.Add($"## Goal: {goal.Name}");
                parts.Add($"**Acceptance Criteria:** {goal.AcceptanceCriteria}");
            }
        }

        parts.Add("");
        parts.Add("Use the available tools to complete this work. Report what you accomplished and any issues encountered.");

        return string.Join("\n", parts);
    }
}
