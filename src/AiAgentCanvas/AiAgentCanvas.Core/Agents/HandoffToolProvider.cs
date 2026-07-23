using System.ComponentModel;
using System.Text.Json;
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Core.Agents;

public sealed class HandoffToolProvider
{
    private readonly IAgentHandoff _handoff;

    public HandoffToolProvider(IAgentHandoff handoff)
    {
        _handoff = handoff;
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
        var result = await _handoff.HandoffAsync(targetAgent, context);
        return JsonSerializer.Serialize(new
        {
            status = result.Status,
            agent = result.Agent,
            response = result.Response,
            error = result.Error,
        });
    }
}
