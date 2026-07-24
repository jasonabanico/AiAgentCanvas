using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Abstractions;

public interface IAuditingChatClientFactory
{
    IChatClient Wrap(IChatClient inner);
}
