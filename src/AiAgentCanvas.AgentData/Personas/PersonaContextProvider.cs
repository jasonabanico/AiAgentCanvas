using Microsoft.Agents.AI;

namespace AiAgentCanvas.AgentData.Personas;

internal sealed class PersonaContextProvider : AIContextProvider
{
    private readonly PersonaStore _store;
    private readonly string _defaultPrompt;

    public PersonaContextProvider(PersonaStore store, string defaultPrompt)
    {
        _store = store;
        _defaultPrompt = defaultPrompt;
    }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken)
    {
        var activeInstructions = _store.GetActiveInstructions();
        if (!string.IsNullOrEmpty(activeInstructions))
        {
            context.AIContext.Instructions = (context.AIContext.Instructions ?? "") + "\n" + activeInstructions;
        }
        else
        {
            if (string.IsNullOrEmpty(context.AIContext.Instructions))
                context.AIContext.Instructions = _defaultPrompt;
        }
        return new ValueTask<AIContext>(context.AIContext);
    }
}
