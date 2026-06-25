using Microsoft.Agents.AI;

namespace AiAgentCanvas.AgentData.Profiles;

internal sealed class UserProfileContextProvider : AIContextProvider
{
    private readonly UserProfileStore _store;

    public UserProfileContextProvider(UserProfileStore store) => _store = store;

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken)
    {
        var profileContext = _store.LoadActiveProfileContext();
        if (!string.IsNullOrEmpty(profileContext))
        {
            context.AIContext.Instructions = (context.AIContext.Instructions ?? "") + profileContext;
        }
        return new ValueTask<AIContext>(context.AIContext);
    }
}
