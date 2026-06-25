using System.ComponentModel;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Core.Agents;

public sealed class HandoffToolProvider
{
    private readonly AgentRegistry _registry;
    private readonly ILogger<HandoffToolProvider> _logger;

    public HandoffToolProvider(AgentRegistry registry, ILogger<HandoffToolProvider> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(HandoffToAgent, "handoff_to_agent",
                "Delegate a task to another agent and get the result back. The target agent runs synchronously and returns its response."),
        ];
    }

    [Description("Delegate a task to another agent. The target agent processes the request and returns its response to you.")]
    private async Task<string> HandoffToAgent(
        [Description("Name of the agent to hand off to (use list_available_agents to see options)")] string targetAgent,
        [Description("The task or question to delegate to the target agent")] string context,
        [Description("Whether the result should be returned to you for further processing (true) or streamed directly to the user (false)")] bool returnToMe = true)
    {
        var agent = _registry.Resolve(targetAgent);
        if (agent is null)
            return JsonSerializer.Serialize(new { error = $"Agent '{targetAgent}' not found. Use list_available_agents to see available agents." });

        _logger.LogInformation("Handing off to agent {Target} with context: {Context}",
            targetAgent, context.Length > 100 ? context[..100] + "..." : context);

        try
        {
            var session = await agent.CreateSessionAsync();
            var messages = new List<ChatMessage> { new(ChatRole.User, context) };
            var response = await agent.RunAsync(messages, session);
            var resultText = response.Text ?? "(no response from agent)";

            _logger.LogInformation("Handoff to {Target} completed. Response length: {Length}", targetAgent, resultText.Length);

            return JsonSerializer.Serialize(new
            {
                status = "completed",
                agent = targetAgent,
                response = resultText,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handoff to agent {Target} failed", targetAgent);
            return JsonSerializer.Serialize(new
            {
                status = "failed",
                agent = targetAgent,
                error = ex.Message,
            });
        }
    }
}
