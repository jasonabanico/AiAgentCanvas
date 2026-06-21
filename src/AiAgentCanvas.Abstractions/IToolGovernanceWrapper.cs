using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Abstractions;

public interface IToolGovernanceWrapper
{
    AIFunction Wrap(AIFunction tool);
}
