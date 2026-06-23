using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Core.Agents;

public sealed class AgentPersonaInfo
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Instructions { get; init; }
}

public sealed class AgentRegistry
{
    private readonly ConcurrentDictionary<string, AIAgent> _agents = new(StringComparer.OrdinalIgnoreCase);
    private readonly IChatClient _chatClient;
    private readonly Func<IEnumerable<AITool>> _toolsFactory;
    private readonly Func<IEnumerable<AIContextProvider>> _contextProvidersFactory;
    private readonly Func<string, AgentPersonaInfo?> _personaLookup;
    private readonly Func<IEnumerable<AgentPersonaInfo>> _personaListAll;
    private readonly ILoggerFactory _loggerFactory;

    public AgentRegistry(
        IChatClient chatClient,
        Func<IEnumerable<AITool>> toolsFactory,
        Func<IEnumerable<AIContextProvider>> contextProvidersFactory,
        Func<string, AgentPersonaInfo?> personaLookup,
        Func<IEnumerable<AgentPersonaInfo>> personaListAll,
        ILoggerFactory loggerFactory)
    {
        _chatClient = chatClient;
        _toolsFactory = toolsFactory;
        _contextProvidersFactory = contextProvidersFactory;
        _personaLookup = personaLookup;
        _personaListAll = personaListAll;
        _loggerFactory = loggerFactory;
    }

    public void RegisterDefault(AIAgent agent)
    {
        _agents["default"] = agent;
    }

    public AIAgent? Resolve(string name)
    {
        if (_agents.TryGetValue(name, out var cached))
            return cached;

        if (name.Equals("default", StringComparison.OrdinalIgnoreCase))
            return _agents.GetValueOrDefault("default");

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
        var tools = _toolsFactory().ToList();
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
