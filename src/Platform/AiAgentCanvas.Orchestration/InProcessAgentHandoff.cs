using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Orchestration;

public sealed class InProcessAgentHandoff : IAgentHandoff
{
    private readonly AgentRegistry _registry;
    private readonly ILogger<InProcessAgentHandoff> _logger;

    public InProcessAgentHandoff(AgentRegistry registry, ILogger<InProcessAgentHandoff> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<HandoffResult> HandoffAsync(string targetAgent, string context, CancellationToken cancellationToken = default)
    {
        var agent = _registry.Resolve(targetAgent);
        if (agent is null)
            return new HandoffResult { Status = "failed", Agent = targetAgent, Error = $"Agent '{targetAgent}' not found. Use list_available_agents to see available agents." };

        _logger.LogInformation("Handing off to agent {Target} with context: {Context}",
            targetAgent, context.Length > 100 ? context[..100] + "..." : context);

        try
        {
            var session = await agent.CreateSessionAsync(cancellationToken);
            var messages = new List<ChatMessage> { new(ChatRole.User, context) };
            var response = await agent.RunAsync(messages, session, cancellationToken: cancellationToken);
            var resultText = response.Text ?? "(no response from agent)";

            _logger.LogInformation("Handoff to {Target} completed. Response length: {Length}", targetAgent, resultText.Length);

            return new HandoffResult { Status = "completed", Agent = targetAgent, Response = resultText };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handoff to agent {Target} failed", targetAgent);
            return new HandoffResult { Status = "failed", Agent = targetAgent, Error = ex.Message };
        }
    }
}
