using AiAgentCanvas.Abstractions;
using AgentGovernance.Audit;
using AgentGovernance.Mcp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Security;

public sealed class GovernanceToolWrapper : IToolGovernanceWrapper
{
    private readonly GovernedMcpGateway _gateway;
    private readonly AuditEmitter _auditEmitter;
    private readonly ILoggerFactory _loggerFactory;

    public GovernanceToolWrapper(
        GovernedMcpGateway gateway,
        AuditEmitter auditEmitter,
        ILoggerFactory loggerFactory)
    {
        _gateway = gateway;
        _auditEmitter = auditEmitter;
        _loggerFactory = loggerFactory;
    }

    public AIFunction Wrap(AIFunction tool)
    {
        return new GovernedAIFunction(
            tool,
            _gateway,
            _auditEmitter,
            _loggerFactory.CreateLogger<GovernedAIFunction>());
    }
}
