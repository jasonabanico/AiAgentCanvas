using System.Collections.Concurrent;
using AiAgentCanvas.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Core.Agents;

public sealed class AgentRegistry : IAgentRegistry
{
    private readonly ConcurrentDictionary<string, AIAgent> _agents = new(StringComparer.OrdinalIgnoreCase);
    private readonly IChatClient _chatClient;
    private readonly Func<IEnumerable<AITool>> _toolsFactory;
    private readonly Func<IEnumerable<AIContextProvider>> _contextProvidersFactory;
    private readonly Func<string, AgentPersonaInfo?> _personaLookup;
    private readonly Func<IEnumerable<AgentPersonaInfo>> _personaListAll;
    private readonly IReadOnlyDictionary<string, IAgentToolsSeed> _toolSeeds;
    private readonly ILoggerFactory _loggerFactory;
    private Func<AIAgent>? _defaultAgentFactory;

    public AgentRegistry(
        IChatClient chatClient,
        Func<IEnumerable<AITool>> toolsFactory,
        Func<IEnumerable<AIContextProvider>> contextProvidersFactory,
        Func<string, AgentPersonaInfo?> personaLookup,
        Func<IEnumerable<AgentPersonaInfo>> personaListAll,
        IReadOnlyDictionary<string, IAgentToolsSeed> toolSeeds,
        ILoggerFactory loggerFactory)
    {
        _chatClient = chatClient;
        _toolsFactory = toolsFactory;
        _contextProvidersFactory = contextProvidersFactory;
        _personaLookup = personaLookup;
        _personaListAll = personaListAll;
        _toolSeeds = toolSeeds;
        _loggerFactory = loggerFactory;
    }

    public void RegisterDefault(AIAgent agent)
    {
        _agents["default"] = agent;
    }

    public void SetDefaultAgentFactory(Func<AIAgent> factory)
    {
        _defaultAgentFactory = factory;
    }

    public AIAgent? Resolve(string name)
    {
        if (_agents.TryGetValue(name, out var cached))
            return cached;

        if (name.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            if (_defaultAgentFactory is { } factory)
            {
                var defaultAgent = factory();
                _agents["default"] = defaultAgent;
                _defaultAgentFactory = null;
                return defaultAgent;
            }
            return _agents.GetValueOrDefault("default");
        }

        var persona = _personaLookup(name);
        if (persona is null) return null;

        var agent = BuildAgentForPersona(persona);
        _agents[name] = agent;
        return agent;
    }

    public void Invalidate(string name)
    {
        _agents.TryRemove(name, out _);
    }

    public IReadOnlyList<string> ListAvailableAgents()
    {
        var fromPersonas = _personaListAll().Select(p => p.Name);
        var registered = _agents.Keys;
        return fromPersonas.Union(registered, StringComparer.OrdinalIgnoreCase).Order().ToList();
    }

    public AgentPersonaInfo? GetAgentInfo(string name)
    {
        if (name.Equals("default", StringComparison.OrdinalIgnoreCase))
            return new AgentPersonaInfo { Name = "default", Description = "Default system agent", Instructions = "(system prompt)" };

        return _personaLookup(name);
    }

    private AIAgent BuildAgentForPersona(AgentPersonaInfo persona)
    {
        var allTools = _toolsFactory().ToList();
        var tools = _toolSeeds.TryGetValue(persona.Name, out var seed)
            ? allTools.Where(t => seed.ToolNames.Contains(t.Name)).ToList()
            : allTools;
        var contextProviders = _contextProvidersFactory().ToList();

        var agentOptions = new ChatClientAgentOptions
        {
            Name = persona.Name,
            Description = persona.Description,
            ChatOptions = new ChatOptions { Tools = tools },
            AIContextProviders = contextProviders.Count > 0 ? contextProviders : null,
        };

        var agent = new ChatClientAgent(_chatClient, agentOptions, _loggerFactory);

        var builder = agent.AsBuilder();
        builder.Use(async (messages, session, runOptions, nextAsync, ct) =>
        {
            var allMessages = messages.ToList();
            allMessages.Insert(0, new ChatMessage(ChatRole.System, persona.Instructions));
            await nextAsync(allMessages, session, runOptions, ct);
        });

        return builder.Build();
    }
}
