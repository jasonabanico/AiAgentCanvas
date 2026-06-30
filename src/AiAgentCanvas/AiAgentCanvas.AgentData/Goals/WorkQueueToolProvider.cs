using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.AgentData.Goals;

public sealed class WorkQueueToolProvider
{
    private readonly WorkQueueStore _store;
    private readonly ILogger<WorkQueueToolProvider> _logger;

    public WorkQueueToolProvider(WorkQueueStore store, ILogger<WorkQueueToolProvider> logger)
    {
        _store = store;
        _logger = logger;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(SubmitWorkItem, "submit_work_item",
                "Submit a work item to the autonomous execution queue"),
            AIFunctionFactory.Create(ListWorkQueue, "list_work_queue",
                "List work items in the queue, optionally filtered by status"),
            AIFunctionFactory.Create(CancelWorkItem, "cancel_work_item",
                "Cancel a pending or claimed work item"),
            AIFunctionFactory.Create(GetQueueStats, "get_queue_stats",
                "Get queue statistics (pending, claimed, completed, failed counts)"),
        ];
    }

    [Description("Submit a work item to the autonomous execution queue")]
    private string SubmitWorkItem(
        [Description("Description of the work to be done")] string description,
        [Description("Priority: critical, high, medium, low")] string priority,
        [Description("Agent persona to assign this to (optional)")] string? assignedAgent = null,
        [Description("Goal this work item contributes to (optional)")] string? goalName = null)
    {
        var id = _store.Submit(description, priority, assignedAgent, goalName);
        _logger.LogInformation("Submitted work item {Id}: {Description}", id, description);

        return JsonSerializer.Serialize(new { status = "submitted", id, description, priority });
    }

    [Description("List work items in the queue")]
    private string ListWorkQueue(
        [Description("Filter by status: pending, claimed, completed, failed, cancelled (leave empty for all)")] string? status = null,
        [Description("Maximum items to return (default: 20)")] int limit = 20)
    {
        var items = _store.List(status, limit);
        return JsonSerializer.Serialize(new
        {
            count = items.Count,
            items = items.Select(i => new
            {
                i.Id,
                i.Description,
                i.Priority,
                i.Status,
                i.AssignedAgent,
                i.GoalName,
                resultPreview = i.Result is not null && i.Result.Length > 150 ? i.Result[..150] + "..." : i.Result,
                i.CreatedAt,
                i.CompletedAt,
            }),
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Cancel a pending or claimed work item")]
    private string CancelWorkItem(
        [Description("The work item ID to cancel")] string id)
    {
        var cancelled = _store.Cancel(id);
        return cancelled
            ? JsonSerializer.Serialize(new { status = "cancelled", id })
            : JsonSerializer.Serialize(new { error = $"Work item '{id}' not found or already completed" });
    }

    [Description("Get queue statistics")]
    private string GetQueueStats()
    {
        var all = _store.List(limit: 1000);
        var stats = all.GroupBy(i => i.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        return JsonSerializer.Serialize(new
        {
            pending = stats.GetValueOrDefault("pending", 0),
            claimed = stats.GetValueOrDefault("claimed", 0),
            completed = stats.GetValueOrDefault("completed", 0),
            failed = stats.GetValueOrDefault("failed", 0),
            cancelled = stats.GetValueOrDefault("cancelled", 0),
            total = all.Count,
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
