using AiAgentCanvas.Core.Skills;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Core.Providers;

public sealed class PlanningMiddleware
{
    private readonly IChatClient _chatClient;
    private readonly DynamicToolRegistry _dynamicTools;
    private readonly IReadOnlyList<AITool> _staticTools;
    private readonly ILogger _logger;

    private const string PlannerPrompt = """
        You are a task planner. Given a user request, conversation history, and available tools,
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

    private const string ContinuationPrompt = """
        You are a task planner tracking progress on a multi-step plan.

        Below is the current plan and the conversation history showing what has been done.
        Determine which steps are complete based on the assistant's prior responses and tool results.

        Respond in this exact format:
        COMPLETED: 1, 2, 3
        REMAINING: 4, 5
        NEXT: 4

        If all steps are complete, respond with exactly: ALL_DONE
        If the plan is no longer relevant to the user's latest message, respond with exactly: REPLAN
        """;

    private const string PlanStateKey = "planner:active_plan";

    public PlanningMiddleware(
        IChatClient chatClient,
        DynamicToolRegistry dynamicTools,
        IEnumerable<IReadOnlyList<AITool>> staticTools,
        ILoggerFactory loggerFactory)
    {
        _chatClient = chatClient;
        _dynamicTools = dynamicTools;
        _staticTools = staticTools.SelectMany(t => t).ToList();
        _logger = loggerFactory.CreateLogger<PlanningMiddleware>();
    }

    public async Task InvokeAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? runOptions,
        Func<IEnumerable<ChatMessage>, AgentSession?, AgentRunOptions?, CancellationToken, Task> nextAsync,
        CancellationToken ct)
    {
        var messageList = messages.ToList();
        var lastUserMessage = messageList.LastOrDefault(m => m.Role == ChatRole.User)?.Text;

        if (string.IsNullOrWhiteSpace(lastUserMessage))
        {
            await nextAsync(messageList, session, runOptions, ct);
            return;
        }

        string? existingPlan = null;
        if (session?.StateBag.TryGetValue<string>(PlanStateKey, out var storedPlan) == true
            && !string.IsNullOrEmpty(storedPlan))
            existingPlan = storedPlan;
        string? planToInject;

        if (existingPlan is not null)
        {
            planToInject = await EvaluateContinuationAsync(existingPlan, messageList, ct);
        }
        else
        {
            planToInject = await GenerateNewPlanAsync(lastUserMessage, ct);
        }

        if (planToInject is not null)
        {
            session?.StateBag.SetValue(PlanStateKey, planToInject);

            messageList.Insert(0, new ChatMessage(ChatRole.System,
                $"Execution plan (follow these steps in order, adapt if a step produces unexpected results):\n{planToInject}"));
        }
        else if (existingPlan is not null)
        {
            session?.StateBag.SetValue(PlanStateKey, string.Empty);
        }

        await nextAsync(messageList, session, runOptions, ct);
    }

    private async Task<string?> GenerateNewPlanAsync(string userMessage, CancellationToken ct)
    {
        var toolSummary = BuildToolSummary();
        if (string.IsNullOrEmpty(toolSummary))
            return null;

        try
        {
            var response = await _chatClient.GetResponseAsync([
                new(ChatRole.System, $"{PlannerPrompt}\n\nAvailable tools:\n{toolSummary}"),
                new(ChatRole.User, userMessage)
            ], new ChatOptions { MaxOutputTokens = 300, Temperature = 0 }, ct);

            var plan = response.Text?.Trim();

            if (string.IsNullOrEmpty(plan) || plan.Contains("NO_PLAN", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Planner: simple request, no plan needed");
                return null;
            }

            _logger.LogInformation("Planner: generated new execution plan");
            return plan;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Planner: failed to generate plan, proceeding without one");
            return null;
        }
    }

    private async Task<string?> EvaluateContinuationAsync(string existingPlan, List<ChatMessage> messages, CancellationToken ct)
    {
        try
        {
            var historyContext = string.Join("\n", messages.TakeLast(10).Select(m =>
                $"{m.Role}: {(m.Text?.Length > 200 ? m.Text[..200] + "..." : m.Text)}"));

            var response = await _chatClient.GetResponseAsync([
                new(ChatRole.System, ContinuationPrompt),
                new(ChatRole.User,
                    $"Current plan:\n{existingPlan}\n\nConversation history:\n{historyContext}")
            ], new ChatOptions { MaxOutputTokens = 150, Temperature = 0 }, ct);

            var result = response.Text?.Trim() ?? "";

            if (result.Contains("ALL_DONE", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Planner: all plan steps complete, clearing plan");
                return null;
            }

            if (result.Contains("REPLAN", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Planner: user changed direction, generating new plan");
                var lastUserMessage = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text;
                return lastUserMessage is not null ? await GenerateNewPlanAsync(lastUserMessage, ct) : null;
            }

            _logger.LogInformation("Planner: continuing existing plan. {Status}", result);
            return $"{existingPlan}\n\nProgress: {result}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Planner: failed to evaluate continuation, reusing existing plan");
            return existingPlan;
        }
    }

    private string BuildToolSummary()
    {
        var allTools = _staticTools.Concat(_dynamicTools.GetAllTools()).ToList();
        if (allTools.Count == 0)
            return string.Empty;

        return string.Join("\n", allTools.Select(t =>
            $"- {t.Name}: {(t is AIFunction fn ? fn.Description : t.Name)}"));
    }
}
