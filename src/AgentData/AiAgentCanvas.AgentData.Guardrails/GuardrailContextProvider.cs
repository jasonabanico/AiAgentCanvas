using Microsoft.Agents.AI;

namespace AiAgentCanvas.AgentData.Guardrails;

internal sealed class GuardrailContextProvider : AIContextProvider
{
    private readonly GuardrailStore _store;

    public GuardrailContextProvider(GuardrailStore store) => _store = store;

    // The agent merges what this returns into the instructions it already has. Returning only
    // the addition keeps each block in the prompt once. Returning the incoming context, which
    // already holds everything earlier providers added, would add all of it a second time.
    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken)
    {
        var rules = _store.LoadActiveRules();
        return new ValueTask<AIContext>(
            string.IsNullOrEmpty(rules) ? new AIContext() : new AIContext { Instructions = rules });
    }
}
