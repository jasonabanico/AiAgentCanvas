using Microsoft.Agents.AI;

namespace AiAgentCanvas.AgentData.Guardrails;

internal sealed class GuardrailContextProvider : AIContextProvider
{
    private readonly GuardrailStore _store;

    public GuardrailContextProvider(GuardrailStore store) => _store = store;

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken)
    {
        var rules = _store.LoadActiveRules();
        if (!string.IsNullOrEmpty(rules))
        {
            context.AIContext.Instructions = (context.AIContext.Instructions ?? "") + rules;
        }
        return new ValueTask<AIContext>(context.AIContext);
    }
}
