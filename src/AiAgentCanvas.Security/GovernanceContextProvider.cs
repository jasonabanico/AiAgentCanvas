using AgentGovernance;
using AgentGovernance.Security;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Security;

public sealed class GovernanceContextProvider : AIContextProvider
{
    private readonly GovernanceKernel _kernel;
    private readonly ILogger<GovernanceContextProvider> _logger;

    public GovernanceContextProvider(GovernanceKernel kernel, ILogger<GovernanceContextProvider> logger)
    {
        _kernel = kernel;
        _logger = logger;
    }

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        if (_kernel.InjectionDetector is null || string.IsNullOrEmpty(context.AIContext.Instructions))
            return new ValueTask<AIContext>(context.AIContext);

        var result = _kernel.InjectionDetector.Detect(context.AIContext.Instructions);
        if (result.IsInjection)
        {
            _logger.LogWarning(
                "[GOVERNANCE] Prompt injection detected in system instructions: {Type}",
                result.InjectionType);

            _kernel.AuditEmitter.Emit(
                AgentGovernance.Audit.GovernanceEventType.PolicyViolation,
                "system",
                "instructions",
                new Dictionary<string, object>
                {
                    ["injectionType"] = result.InjectionType.ToString(),
                    ["source"] = "system_instructions",
                });
        }

        return new ValueTask<AIContext>(context.AIContext);
    }
}
