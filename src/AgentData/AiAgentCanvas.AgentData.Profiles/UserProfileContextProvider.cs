using Microsoft.Agents.AI;

namespace AiAgentCanvas.AgentData.Profiles;

internal sealed class UserProfileContextProvider : AIContextProvider
{
    private readonly UserProfileStore _store;

    public UserProfileContextProvider(UserProfileStore store) => _store = store;

    // The agent merges what this returns into the instructions it already has. Returning only
    // the addition keeps each block in the prompt once. Returning the incoming context, which
    // already holds everything earlier providers added, would add all of it a second time.
    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken)
    {
        var profileContext = _store.LoadActiveProfileContext();
        return new ValueTask<AIContext>(
            string.IsNullOrEmpty(profileContext) ? new AIContext() : new AIContext { Instructions = profileContext });
    }
}
