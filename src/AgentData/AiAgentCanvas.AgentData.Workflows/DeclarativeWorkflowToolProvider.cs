using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.AgentData.Workflows;

public sealed class DeclarativeWorkflowToolProvider
{
    private readonly DeclarativeWorkflowExecutor _executor;
    private readonly ILogger<DeclarativeWorkflowToolProvider> _logger;

    public DeclarativeWorkflowToolProvider(DeclarativeWorkflowExecutor executor, ILogger<DeclarativeWorkflowToolProvider> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(ListDeclarativeWorkflows, "list_declarative_workflows",
                "List all YAML-defined declarative workflows"),
            AIFunctionFactory.Create(RunDeclarativeWorkflow, "run_declarative_workflow",
                "Execute a YAML-defined declarative workflow"),
        ];
    }

    [Description("List all YAML-defined declarative workflows")]
    private string ListDeclarativeWorkflows()
    {
        var workflows = _executor.ListDeclarativeWorkflows();
        return JsonSerializer.Serialize(new { count = workflows.Count, workflows });
    }

    [Description("Execute a YAML-defined declarative workflow")]
    private async Task<string> RunDeclarativeWorkflow(
        [Description("Name of the YAML workflow file (without extension)")] string name,
        [Description("Input message for the workflow")] string input,
        CancellationToken ct)
    {
        return await _executor.ExecuteAsync(name, input, ct);
    }
}
