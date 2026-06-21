using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.AgentData.Workflows;

public sealed class WorkflowToolProvider
{
    private readonly WorkflowStore _store;
    private readonly WorkflowExecutor _executor;
    private readonly ILogger<WorkflowToolProvider> _logger;

    public WorkflowToolProvider(WorkflowStore store, WorkflowExecutor executor, ILogger<WorkflowToolProvider> logger)
    {
        _store = store;
        _executor = executor;
        _logger = logger;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(CreateWorkflow, "create_workflow",
                "Create a new multi-step workflow definition"),
            AIFunctionFactory.Create(ListWorkflows, "list_workflows",
                "List all saved workflow definitions"),
            AIFunctionFactory.Create(ReadWorkflow, "read_workflow",
                "Read the full definition of a workflow"),
            AIFunctionFactory.Create(RunWorkflow, "run_workflow",
                "Execute a saved workflow"),
            AIFunctionFactory.Create(DeleteWorkflow, "delete_workflow",
                "Delete a workflow definition"),
        ];
    }

    [Description("Create a new multi-step workflow definition")]
    private string CreateWorkflow(
        [Description("Name of the workflow (e.g. 'daily-market-scan')")] string name,
        [Description("Short description of what the workflow does")] string description,
        [Description("Workflow steps in markdown format")] string steps,
        [Description("Comma-separated tags for categorization")] string? tags)
    {
        var existing = _store.Get(name);
        if (existing is not null)
            return JsonSerializer.Serialize(new { error = $"Workflow '{name}' already exists." });

        _store.Save(name, description, tags, steps);
        _logger.LogInformation("Created workflow {Name}", name);

        return JsonSerializer.Serialize(new { status = "created", name });
    }

    [Description("List all saved workflow definitions")]
    private string ListWorkflows()
    {
        var workflows = _store.ListAll();
        return JsonSerializer.Serialize(new
        {
            count = workflows.Count,
            workflows = workflows.Select(w => new
            {
                w.Name,
                w.Description,
                w.Tags,
            }),
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Read the full definition of a workflow")]
    private string ReadWorkflow(
        [Description("The name of the workflow to read")] string name)
    {
        var workflow = _store.Get(name);
        if (workflow is null)
            return JsonSerializer.Serialize(new { error = $"Workflow '{name}' not found" });

        return JsonSerializer.Serialize(new
        {
            workflow.Name,
            workflow.Description,
            workflow.Tags,
            workflow.Content,
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Execute a saved workflow with optional input")]
    private async Task<string> RunWorkflow(
        [Description("The name of the workflow to run")] string name,
        [Description("Optional input to pass to the workflow")] string? input,
        CancellationToken ct)
    {
        return await _executor.ExecuteAsync(name, input, ct);
    }

    [Description("Delete a workflow definition")]
    private string DeleteWorkflow(
        [Description("The name of the workflow to delete")] string name)
    {
        var deleted = _store.Delete(name);
        return deleted
            ? JsonSerializer.Serialize(new { status = "deleted", name })
            : JsonSerializer.Serialize(new { error = $"Workflow '{name}' not found" });
    }
}
