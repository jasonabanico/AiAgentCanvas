using Microsoft.Agents.AI;

namespace AiAgentCanvas.AgentData.Entities;

internal sealed class EntityContextProvider : AIContextProvider
{
    private readonly EntityStore _store;

    public EntityContextProvider(EntityStore store) => _store = store;

    // The agent merges what this returns into the instructions it already has. Returning only
    // the addition keeps each block in the prompt once. Returning the incoming context, which
    // already holds everything earlier providers added, would add all of it a second time.
    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken)
    {
        var index = _store.LoadEntityIndex();
        return new ValueTask<AIContext>(
            string.IsNullOrEmpty(index) ? new AIContext() : new AIContext { Instructions = index });
    }
}
