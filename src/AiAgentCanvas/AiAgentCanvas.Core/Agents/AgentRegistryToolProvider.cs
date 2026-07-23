using System.ComponentModel;
using System.Text.Json;
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Core.Agents;

public sealed class AgentRegistryToolProvider
{
    private readonly IAgentRegistry _registry;

    public AgentRegistryToolProvider(IAgentRegistry registry)
    {
        _registry = registry;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(ListAvailableAgents, "list_available_agents",
                "List all agents available for handoff or messaging"),
            AIFunctionFactory.Create(GetAgentInfo, "get_agent_info",
                "Get details about a specific agent (persona name, description, instructions)"),
        ];
    }

    [Description("List all agents available for handoff or messaging")]
    private string ListAvailableAgents()
    {
        var agents = _registry.ListAvailableAgents();
        return JsonSerializer.Serialize(new
        {
            count = agents.Count,
            agents = agents.Select(name =>
            {
                var info = _registry.GetAgentInfo(name);
                return new { name, description = info?.Description ?? "" };
            }),
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Get details about a specific agent")]
    private string GetAgentInfo(
        [Description("The agent name (persona name)")] string name)
    {
        var info = _registry.GetAgentInfo(name);
        if (info is null)
            return JsonSerializer.Serialize(new { error = $"Agent '{name}' not found" });

        return JsonSerializer.Serialize(new
        {
            info.Name,
            info.Description,
            instructionsPreview = info.Instructions.Length > 200 ? info.Instructions[..200] + "..." : info.Instructions,
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
