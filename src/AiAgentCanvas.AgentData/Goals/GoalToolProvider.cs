using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.AgentData.Goals;

public sealed class GoalToolProvider
{
    private readonly GoalStore _store;
    private readonly ILogger<GoalToolProvider> _logger;

    public GoalToolProvider(GoalStore store, ILogger<GoalToolProvider> logger)
    {
        _store = store;
        _logger = logger;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(CreateGoal, "create_goal",
                "Create a new goal for autonomous execution"),
            AIFunctionFactory.Create(ListGoals, "list_goals",
                "List all goals, optionally filtered by status"),
            AIFunctionFactory.Create(ReadGoal, "read_goal",
                "Read the full details of a goal"),
            AIFunctionFactory.Create(UpdateGoalStatus, "update_goal_status",
                "Update a goal's status (active, completed, blocked, cancelled)"),
            AIFunctionFactory.Create(DeleteGoal, "delete_goal",
                "Delete a goal"),
        ];
    }

    [Description("Create a new goal for autonomous execution")]
    private string CreateGoal(
        [Description("Short name for the goal (kebab-case, e.g. 'process-overdue-dsars')")] string name,
        [Description("What the goal aims to achieve")] string description,
        [Description("Priority: critical, high, medium, low")] string priority,
        [Description("Measurable criteria that define when the goal is complete")] string acceptanceCriteria,
        [Description("Name of the agent persona to assign this goal to (optional)")] string? assignedAgent,
        [Description("Detailed instructions for how to accomplish the goal")] string content)
    {
        var existing = _store.Get(name);
        if (existing is not null)
            return JsonSerializer.Serialize(new { error = $"Goal '{name}' already exists." });

        _store.Save(name, description, priority, "active", acceptanceCriteria, assignedAgent, content);
        _logger.LogInformation("Created goal {Name} with priority {Priority}", name, priority);

        return JsonSerializer.Serialize(new { status = "created", name, priority });
    }

    [Description("List all goals, optionally filtered by status")]
    private string ListGoals(
        [Description("Filter by status: active, completed, blocked, cancelled (leave empty for all)")] string? status)
    {
        var goals = string.IsNullOrWhiteSpace(status) ? _store.ListAll() : _store.ListAll()
            .Where(g => g.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return JsonSerializer.Serialize(new
        {
            count = goals.Count,
            goals = goals.Select(g => new
            {
                g.Name,
                g.Description,
                g.Priority,
                g.Status,
                g.AcceptanceCriteria,
                g.AssignedAgent,
            }),
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Read the full details of a goal")]
    private string ReadGoal(
        [Description("The name of the goal to read")] string name)
    {
        var goal = _store.Get(name);
        if (goal is null)
            return JsonSerializer.Serialize(new { error = $"Goal '{name}' not found" });

        return JsonSerializer.Serialize(new
        {
            goal.Name,
            goal.Description,
            goal.Priority,
            goal.Status,
            goal.AcceptanceCriteria,
            goal.AssignedAgent,
            goal.Content,
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Update a goal's status")]
    private string UpdateGoalStatus(
        [Description("The name of the goal")] string name,
        [Description("New status: active, completed, blocked, cancelled")] string newStatus)
    {
        var updated = _store.UpdateStatus(name, newStatus);
        if (!updated)
            return JsonSerializer.Serialize(new { error = $"Goal '{name}' not found" });

        _logger.LogInformation("Updated goal {Name} status to {Status}", name, newStatus);
        return JsonSerializer.Serialize(new { status = "updated", name, newStatus });
    }

    [Description("Delete a goal")]
    private string DeleteGoal(
        [Description("The name of the goal to delete")] string name)
    {
        var deleted = _store.Delete(name);
        return deleted
            ? JsonSerializer.Serialize(new { status = "deleted", name })
            : JsonSerializer.Serialize(new { error = $"Goal '{name}' not found" });
    }
}
