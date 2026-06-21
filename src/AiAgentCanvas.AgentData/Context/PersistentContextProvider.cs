using Microsoft.Agents.AI;

namespace AiAgentCanvas.AgentData.Context;

internal sealed class PersistentContextProvider : AIContextProvider
{
    private readonly ContextStore _store;

    public PersistentContextProvider(ContextStore store) => _store = store;

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken)
    {
        var content = _store.LoadAllContent();
        if (!string.IsNullOrEmpty(content))
        {
            context.AIContext.Instructions = (context.AIContext.Instructions ?? "") + content;
        }
        return new ValueTask<AIContext>(context.AIContext);
    }
}
