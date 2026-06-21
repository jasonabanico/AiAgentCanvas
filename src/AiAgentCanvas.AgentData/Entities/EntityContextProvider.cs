using Microsoft.Agents.AI;

namespace AiAgentCanvas.AgentData.Entities;

internal sealed class EntityContextProvider : AIContextProvider
{
    private readonly EntityStore _store;

    public EntityContextProvider(EntityStore store) => _store = store;

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken)
    {
        var index = _store.LoadEntityIndex();
        if (!string.IsNullOrEmpty(index))
        {
            context.AIContext.Instructions = (context.AIContext.Instructions ?? "") + index;
        }
        return new ValueTask<AIContext>(context.AIContext);
    }
}
