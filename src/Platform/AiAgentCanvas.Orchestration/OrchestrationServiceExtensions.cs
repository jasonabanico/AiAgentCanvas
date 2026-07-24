#pragma warning disable MAAI001
#pragma warning disable MEAI001

using AiAgentCanvas.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Orchestration;

public static class OrchestrationServiceExtensions
{
    public static IServiceCollection AddAiAgentCanvasInterAgentCommunication(
        this IServiceCollection services,
        Func<IServiceProvider, Func<string, AgentPersonaInfo?>> personaLookupFactory,
        Func<IServiceProvider, Func<IEnumerable<AgentPersonaInfo>>> personaListAllFactory,
        string agentName = "AiAgentCanvas")
    {
        services.AddSingleton<IAgentMessaging, InProcessAgentMessaging>();

        services.AddSingleton(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var toolsFactory = () => sp.GetServices<IReadOnlyList<AITool>>().SelectMany(t => t);
            var contextProvidersFactory = () => sp.GetServices<AIContextProvider>();
            var personaLookup = personaLookupFactory(sp);
            var personaListAll = personaListAllFactory(sp);
            var toolSeeds = sp.GetServices<IAgentToolsSeed>().ToDictionary(s => s.AgentName, StringComparer.OrdinalIgnoreCase);

            var httpClientFactory = sp.GetService<IHttpClientFactory>();
            var registry = new AgentRegistry(chatClient, toolsFactory, contextProvidersFactory,
                personaLookup, personaListAll, toolSeeds, loggerFactory, httpClientFactory);

            registry.SetDefaultAgentFactory(() => sp.GetRequiredService<AIAgent>());
            return registry;
        });
        services.AddSingleton<IAgentRegistry>(sp => sp.GetRequiredService<AgentRegistry>());
        services.AddSingleton<IAgentHandoff, InProcessAgentHandoff>();

        services.AddA2AServer(agentName);
        services.AddKeyedSingleton<AIAgent>(agentName, (sp, _) => sp.GetRequiredService<AIAgent>());

        services.AddSingleton<AgentRegistryToolProvider>();
        services.AddSingleton<HandoffToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<AgentRegistryToolProvider>().GetTools());
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<HandoffToolProvider>().GetTools());

        services.AddSingleton(new ToolStateMapping("list_available_agents", ToolStateBehavior.Snapshot));

        return services;
    }

    public static WebApplication MapA2AEndpoints(this WebApplication app, string agentName = "AiAgentCanvas", string path = "/a2a")
    {
        app.MapA2AHttpJson(agentName, path);
        return app;
    }
}
