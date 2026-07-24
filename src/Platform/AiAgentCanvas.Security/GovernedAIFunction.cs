using AgentGovernance.Audit;
using AgentGovernance.Audit;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Security;

public sealed class GovernedAIFunction : DelegatingAIFunction
{
    private readonly GovernedMcpGateway _gateway;
    private readonly AuditEmitter _auditEmitter;
    private readonly ILogger _logger;

    public GovernedAIFunction(
        AIFunction inner,
        GovernedMcpGateway gateway,
        AuditEmitter auditEmitter,
        ILogger logger)
        : base(inner)
    {
        _gateway = gateway;
        _auditEmitter = auditEmitter;
        _logger = logger;
    }

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var toolName = InnerFunction.Name;
        var payload = arguments.Count > 0
            ? System.Text.Json.JsonSerializer.Serialize(
                arguments.ToDictionary(kv => kv.Key, kv => kv.Value))
            : null;

        var decision = _gateway.Evaluate("agent", toolName, payload);

        _auditEmitter.Emit(
            decision.Allowed ? GovernanceEventType.PolicyCheck : GovernanceEventType.ToolCallBlocked,
            "agent",
            toolName,
            new Dictionary<string, object>
            {
                ["status"] = decision.Status.ToString(),
                ["allowed"] = decision.Allowed,
            });

        if (!decision.Allowed)
        {
            _logger.LogWarning("[GOVERNANCE] Tool call blocked: {Tool}, status={Status}", toolName, decision.Status);
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                error = $"Tool '{toolName}' was blocked by governance policy.",
                status = decision.Status.ToString(),
            });
        }

        _logger.LogDebug("[GOVERNANCE] Tool call allowed: {Tool}", toolName);
        return await base.InvokeCoreAsync(arguments, cancellationToken);
    }
}
