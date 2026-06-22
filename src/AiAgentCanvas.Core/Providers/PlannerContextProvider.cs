using AiAgentCanvas.Core.Skills;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Core.Providers;

public sealed class PlannerContextProvider : AIContextProvider
{
    private readonly IChatClient _chatClient;
    private readonly DynamicToolRegistry _dynamicTools;
    private readonly IReadOnlyList<AITool> _staticTools;
    private readonly ILogger<PlannerContextProvider> _logger;

    private const string PlannerSystemPrompt = """
        You are a task planner. Given a user request and a list of available tools,
        decide whether the request requires multiple steps to fulfill.

        If the request is simple (can be answered directly or with 1-2 tool calls),
        respond with exactly: NO_PLAN

        If the request is complex (requires 3+ steps, sequencing, or intermediate
        decisions based on earlier results), decompose it into numbered steps.
        Each step should specify:
        - What to do
        - Which tool to use (if applicable)
        - What to do with the result before proceeding

        Keep the plan concise. Do not explain your reasoning.
        """;

    public PlannerContextProvider(
        IChatClient chatClient,
        DynamicToolRegistry dynamicTools,
        IEnumerable<IReadOnlyList<AITool>> staticTools,
        ILogger<PlannerContextProvider> logger)
    {
        _chatClient = chatClient;
        _dynamicTools = dynamicTools;
        _staticTools = staticTools.SelectMany(t => t).ToList();
        _logger = logger;
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        var lastUserMessage = context.AIContext.Messages?
            .LastOrDefault(m => m.Role == ChatRole.User)?.Text;

        if (string.IsNullOrWhiteSpace(lastUserMessage))
            return context.AIContext;

        var toolSummary = BuildToolSummary();
        if (string.IsNullOrEmpty(toolSummary))
            return context.AIContext;

        try
        {
            var planResponse = await _chatClient.GetResponseAsync([
                new(ChatRole.System, $"{PlannerSystemPrompt}\n\nAvailable tools:\n{toolSummary}"),
                new(ChatRole.User, lastUserMessage)
            ], new ChatOptions { MaxOutputTokens = 300, Temperature = 0 }, cancellationToken);

            var plan = planResponse.Text?.Trim();

            if (string.IsNullOrEmpty(plan) || plan.Contains("NO_PLAN", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Planner: simple request, no plan needed");
                return context.AIContext;
            }

            _logger.LogInformation("Planner: generated execution plan for complex request");
            context.AIContext.Instructions += $"\n\nExecution plan (follow these steps in order, "
                + $"adapt if a step produces unexpected results):\n{plan}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Planner: failed to generate plan, proceeding without one");
        }

        return context.AIContext;
    }

    private string BuildToolSummary()
    {
        var allTools = _staticTools.Concat(_dynamicTools.GetAllTools()).ToList();
        if (allTools.Count == 0)
            return string.Empty;

        return string.Join("\n", allTools.Select(t =>
            $"- {t.Name}: {(t is AIFunction fn ? fn.Description : t.Name)}")
        );
    }
}
