using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.AgentData.Workflows;

public sealed class WorkflowExecutor
{
    private readonly WorkflowStore _store;
    private readonly IServiceProvider _sp;
    private readonly ILogger<WorkflowExecutor> _logger;

    public WorkflowExecutor(WorkflowStore store, IServiceProvider sp, ILogger<WorkflowExecutor> logger)
    {
        _store = store;
        _sp = sp;
        _logger = logger;
    }

    public async Task<string> ExecuteAsync(string workflowName, string? userInput, CancellationToken ct)
    {
        var workflow = _store.Get(workflowName);
        if (workflow is null)
            return JsonSerializer.Serialize(new { error = $"Workflow '{workflowName}' not found" });

        _logger.LogInformation("Executing workflow {Name}", workflowName);

        var prompt = new StringBuilder();
        prompt.AppendLine($"Execute the following workflow: {workflow.Name}");
        prompt.AppendLine($"Description: {workflow.Description}");
        prompt.AppendLine();
        prompt.AppendLine("Workflow steps:");
        prompt.AppendLine(workflow.Content);

        if (!string.IsNullOrWhiteSpace(userInput))
        {
            prompt.AppendLine();
            prompt.AppendLine($"User input: {userInput}");
        }

        prompt.AppendLine();
        prompt.AppendLine("Execute each step in order using the available tools. Report the result of each step.");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, prompt.ToString()),
        };

        var innerClient = _sp.GetRequiredService<IChatClient>();
        var tools = _sp.GetServices<IReadOnlyList<AITool>>().SelectMany(t => t).ToList();
        using var chatClient = new FunctionInvokingChatClient(innerClient);
        var options = new ChatOptions { Tools = tools };
        var response = await chatClient.GetResponseAsync(messages, options, ct);
        var responseText = response.Text ?? string.Empty;

        _logger.LogInformation("Workflow {Name} completed, response length {Length}", workflowName, responseText.Length);

        return JsonSerializer.Serialize(new { workflow = workflowName, result = responseText });
    }
}
