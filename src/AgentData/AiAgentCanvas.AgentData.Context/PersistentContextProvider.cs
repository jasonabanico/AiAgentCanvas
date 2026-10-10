using Microsoft.Agents.AI;

namespace AiAgentCanvas.AgentData.Context;

internal sealed class PersistentContextProvider : AIContextProvider
{
    private readonly ContextStore _store;

    public PersistentContextProvider(ContextStore store) => _store = store;

    // The agent merges what this returns into the instructions it already has. Returning only
    // the addition keeps each block in the prompt once. Returning the incoming context, which
    // already holds everything earlier providers added, would add all of it a second time.
    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken)
    {
        var content = _store.LoadAllContent();
        return new ValueTask<AIContext>(
            string.IsNullOrEmpty(content) ? new AIContext() : new AIContext { Instructions = content });
    }
}
