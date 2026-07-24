# 8. Multi-Agent Setup

AI Agent Canvas supports three topologies for running multiple agents. Each offers different trade-offs between simplicity, isolation, and scalability. You can combine them -- use in-process handoff and background delegation within a single host, then connect hosts with A2A when you need independent deployment.

## Option 1: In-Process Handoff

All agents run in the same .NET process. The main agent delegates to a specialist by calling the `handoff_to_agent` tool. The specialist runs to completion and returns its response directly.

Under the hood, the flow is:

1. `HandoffToolProvider` exposes the `handoff_to_agent` tool to the LLM
2. `InProcessAgentHandoff` receives the call with the target agent name and context
3. `AgentRegistry.Resolve` finds or builds the target agent (loading its persona and scoped tools)
4. The target agent runs as a `ChatClientAgent` with its own persona instructions
5. The response flows back to the calling agent as a tool result

**When to use:** Multiple specialists without deployment complexity. Shared LLM endpoint. Low latency between agents. All agents owned by the same team.

### Setup

Register a persona seed and tool seed for each specialist agent. The `AgentRegistry` builds agents on demand from these seeds.

```csharp
public static IServiceCollection AddFinancialAnalyst(this IServiceCollection services)
{
    // Persona seed defines the agent's identity and instructions
    services.AddSingleton<IPersonaSeed>(new PersonaSeed(
        name: "financial-analyst",
        description: "A financial analyst that uses market data tools to research stocks and generate reports.",
        instructions: "You are a financial analyst assistant. Use the available market data tools to research stocks, analyze trends, and generate investment reports."));

    // Tool seed restricts which tools this agent can see
    services.AddSingleton<IAgentToolsSeed>(new AgentToolsSeed(
        agentName: "financial-analyst",
        toolNames: ["stock_quote", "stock_history", "edgar_company_facts"]));

    return services;
}
```

The handoff tool is registered automatically by `HandoffToolProvider`. Any persona in the registry is available as a handoff target:

```csharp
// HandoffToolProvider exposes this to the LLM
[Description("Delegate a task to another agent and get the result back.")]
private async Task<string> HandoffToAgent(string targetAgent, string context)
{
    var result = await _handoff.HandoffAsync(targetAgent, context);
    return JsonSerializer.Serialize(result);
}
```

When the LLM decides to delegate, it calls `handoff_to_agent` with the target name and a context message. The specialist runs in the same process, and the result comes back as a tool response.

## Option 2: Background Delegation

The main agent spawns parallel tasks on background agents. This is asynchronous -- the agent can start multiple tasks, continue working on other things, and collect results when they finish.

Background delegation exposes five tools to the LLM:

| Tool | Purpose |
|---|---|
| `background_agents_start_task` | Start a task on a named background agent |
| `background_agents_wait_for_first_completion` | Block until the first background task finishes |
| `background_agents_get_task_results` | Get the result of a specific completed task |
| `background_agents_get_all_tasks` | List all background tasks and their status |
| `background_agents_continue_task` | Send a follow-up message to a running task |

**When to use:** Independent parallel tasks (research multiple topics simultaneously). Synthesizing results from multiple specialists. Tasks where the LLM decides at runtime which agents to involve.

### Setup

Background delegation is automatic. Any persona registered in `AgentRegistry` is available as a background agent. The platform resolves all non-default agents and passes them to the `HarnessAgent`:

```csharp
var backgroundAgents = registry.ListAvailableAgents()
    .Where(n => !n.Equals("default", StringComparison.OrdinalIgnoreCase))
    .Select(n => registry.Resolve(n))
    .Where(a => a is not null)
    .ToList()!;

AIAgent agent = chatClient.AsHarnessAgent(new HarnessAgentOptions
{
    Name = options.AgentName,
    BackgroundAgents = backgroundAgents is { Count: > 0 } ? backgroundAgents : null,
    // ...
});
```

No additional configuration needed. Register persona and tool seeds, and the agent becomes available for both handoff and background delegation.

### Example: Parallel Research

The LLM might use background delegation like this during a conversation:

```
User: Compare the financial health of Apple, Microsoft, and Google.

Agent (internal reasoning):
  -> background_agents_start_task("financial-analyst", "Analyze Apple's financial health using AAPL data")
  -> background_agents_start_task("financial-analyst", "Analyze Microsoft's financial health using MSFT data")
  -> background_agents_start_task("financial-analyst", "Analyze Google's financial health using GOOGL data")
  -> background_agents_wait_for_first_completion()
  -> (collects all three results)
  -> Synthesizes a comparative report
```

## Option 3: A2A Protocol

Separate hosts communicating over HTTP. Each host exposes an `/a2a` endpoint and publishes `AgentCard` metadata describing its capabilities. Remote agents behave like local ones -- handoff and background tools work transparently.

**When to use:** Independent deployment and scaling per agent. Different security boundaries. Different teams owning different agents. Third-party agents from external organizations.

### Setup

Each host runs its own instance of AI Agent Canvas with its own agents. The host exposes the A2A endpoint:

```csharp
// In Program.cs of the remote agent host
var app = builder.Build();
app.UseAiAgentCanvas();
app.MapA2AEndpoints(agentName: "ComplianceAgent", path: "/a2a");
app.Run();
```

The A2A server is registered in DI:

```csharp
services.AddA2AServer(agentName);
```

The orchestrator registers remote agents by URL. They join the registry and become available through the same handoff and background tools:

```csharp
// In the orchestrator's startup
var registry = app.Services.GetRequiredService<AgentRegistry>();
registry.RegisterRemote(
    name: "compliance-checker",
    a2aUrl: "https://compliance-service.internal/a2a",
    description: "Reviews documents for regulatory compliance");
```

The `RegisterRemote` method creates an `AgentCard`, configures an `HttpClient` with the remote URL, and builds an agent proxy:

```csharp
public void RegisterRemote(string name, string a2aUrl, string? description = null)
{
    var card = new AgentCard
    {
        Name = name,
        Description = description ?? $"Remote A2A agent at {a2aUrl}",
        Version = "1.0"
    };
    var httpClient = _httpClientFactory?.CreateClient(name) ?? new HttpClient();
    httpClient.BaseAddress = new Uri(a2aUrl);
    var agent = card.AsAIAgent(httpClient, loggerFactory: _loggerFactory);
    _agents[name] = agent;
}
```

After registration, `handoff_to_agent("compliance-checker", ...)` sends the request over HTTP to the remote host. The calling agent does not need to know whether the target is local or remote.

## Comparison

| | In-Process Handoff | Background Delegation | A2A Protocol |
|---|---|---|---|
| **Latency** | Lowest (in-memory) | Low (in-memory, async overhead) | Higher (HTTP round-trip) |
| **Isolation** | None (shared process) | None (shared process) | Full (separate hosts) |
| **Parallelism** | Sequential only | Parallel tasks | Parallel via separate hosts |
| **Scaling** | Single process | Single process | Independent per host |
| **Complexity** | Low | Low | Moderate (deployment, networking) |
| **Security boundary** | Shared | Shared | Independent (separate auth, policies) |

## Composing Topologies

These options are not mutually exclusive. A practical setup might look like:

- **In-process + background** for your team's agents: a financial analyst, a report generator, and a data researcher all running in one host, using handoff for sequential work and background delegation for parallel tasks.
- **A2A** for other teams' agents: the compliance team runs their own host with their own deployment schedule and security policies. Your orchestrator connects to it via `RegisterRemote`.

The agent code does not change between topologies. A persona that works as an in-process handoff target works identically as a remote A2A agent. The difference is in how you register it.
