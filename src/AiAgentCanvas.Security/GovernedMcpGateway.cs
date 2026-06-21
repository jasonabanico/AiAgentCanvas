using AgentGovernance.Mcp;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Security;

public sealed class GovernedMcpGateway
{
    private readonly McpGateway _gateway;
    private readonly ILogger<GovernedMcpGateway> _logger;

    public GovernedMcpGateway(McpGatewayConfig config, ILogger<GovernedMcpGateway> logger)
    {
        _gateway = new McpGateway(config);
        _logger = logger;
    }

    public McpGatewayDecision Evaluate(string agentId, string toolName, string? payload = null)
    {
        var request = new McpGatewayRequest
        {
            AgentId = agentId,
            ToolName = toolName,
            Payload = payload ?? "",
        };

        var decision = _gateway.ProcessRequest(request);

        if (!decision.Allowed)
        {
            _logger.LogWarning(
                "[GOVERNANCE:MCP] Blocked tool={Tool} agent={Agent} status={Status}",
                toolName, agentId, decision.Status);
        }

        return decision;
    }
}
