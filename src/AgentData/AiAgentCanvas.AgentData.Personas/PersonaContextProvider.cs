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

    // The agent merges what this returns into the instructions it already has. Returning only
    // the addition keeps each block in the prompt once. Returning the incoming context, which
    // already holds everything earlier providers added, would add all of it a second time.
    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken)
    {
        var activeInstructions = _store.GetActiveInstructions();
        if (!string.IsNullOrEmpty(activeInstructions))
            return new ValueTask<AIContext>(new AIContext { Instructions = activeInstructions });

        // No persona is active, so the default prompt stands in when nothing else has set one.
        return new ValueTask<AIContext>(string.IsNullOrEmpty(context.AIContext.Instructions)
            ? new AIContext { Instructions = _defaultPrompt }
            : new AIContext());
    }
}
