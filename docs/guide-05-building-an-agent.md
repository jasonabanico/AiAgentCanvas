# 5. Building an Agent

An agent in AI Agent Canvas is a thin set of seed classes that declare what the agent *is* and what it *knows*. You write six pieces of custom code. The platform handles everything else: runtime execution, streaming, context injection, tool routing, governance, telemetry, and more.

## What You Implement

### 1. A Persona Seed

The persona defines the agent's identity, role, and behavioral instructions. It implements `IPersonaSeed` using the `PersonaSeed` class, which takes a name, description, and freeform instructions string.

```csharp
services.AddSingleton<IPersonaSeed>(new PersonaSeed(
    name: "financial-analyst",
    description: "A financial analyst that uses market data tools to research stocks and companies",
    instructions: """
        You are a financial analyst assistant.

        You have access to these tools:
        - stock_quote: Get current price and daily change for a ticker from Yahoo Finance
        - stock_history: Get historical price data with configurable range (5d, 1mo, 3mo, 6mo, 1y, 5y)
        - edgar_company_facts: Fetch SEC EDGAR financial data including revenue, net income, assets, and EPS

        When analyzing a stock:
        1. Start with stock_quote for the current price and daily movement
        2. Use stock_history to show recent trends
        3. Use edgar_company_facts for fundamental data (revenue, earnings, balance sheet)
        4. Synthesize the data into a clear summary with specific numbers

        Always cite the data source (Yahoo Finance or SEC EDGAR) and note that this is informational,
        not investment advice.
        """));
```

The interface is minimal:

```csharp
public interface IPersonaSeed
{
    string Name { get; }
    string Description { get; }
    string Instructions { get; }
}
```

On first startup, the persona is saved to `agent-data/orchestrator/agent/personas/financial-analyst.md`. If that file already exists, the seed is skipped -- manual edits are preserved.

### 2. Tool Providers

Tools are registered in a separate data connection project, not in the agent itself. This separation means multiple agents can share the same tools without duplication.

A tool provider is a class that returns `IReadOnlyList<AITool>` using `AIFunctionFactory.Create`. Each method uses `[Description]` attributes so the LLM understands what the tool does and what each parameter means.

```csharp
public sealed class MarketDataToolProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MarketDataToolProvider> _logger;

    public MarketDataToolProvider(IHttpClientFactory httpClientFactory, ILogger<MarketDataToolProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(EdgarCompanyFactsAsync, "edgar_company_facts",
                "Fetch SEC EDGAR financial data for a company by ticker symbol"),
            AIFunctionFactory.Create(StockQuoteAsync, "stock_quote",
                "Get current stock price and daily change from Yahoo Finance"),
            AIFunctionFactory.Create(StockHistoryAsync, "stock_history",
                "Get historical price data from Yahoo Finance"),
        ];
    }

    [Description("Fetch SEC EDGAR financial data for a company by ticker symbol")]
    private async Task<string> EdgarCompanyFactsAsync(
        [Description("Stock ticker symbol (e.g. AAPL, MSFT)")] string ticker,
        CancellationToken ct)
    {
        // Implementation: call SEC EDGAR API, parse response, return JSON summary
    }

    [Description("Get current stock price and daily change from Yahoo Finance")]
    private async Task<string> StockQuoteAsync(
        [Description("Stock ticker symbol")] string ticker,
        CancellationToken ct)
    {
        // Implementation: call Yahoo Finance API
    }

    [Description("Get historical price data from Yahoo Finance")]
    private async Task<string> StockHistoryAsync(
        [Description("Stock ticker symbol")] string ticker,
        [Description("Time range: 5d, 1mo, 3mo, 6mo, 1y, 5y")] string range,
        CancellationToken ct)
    {
        // Implementation: call Yahoo Finance historical data API
    }
}
```

The tool provider is registered in DI as `IReadOnlyList<AITool>`:

```csharp
services.AddSingleton<MarketDataToolProvider>();
services.AddSingleton<IReadOnlyList<AITool>>(sp =>
    sp.GetRequiredService<MarketDataToolProvider>().GetTools());
```

### 3. An Agent Tools Seed

The agent tools seed declares which tools the agent depends on by name. This creates an explicit binding between the agent and its tools that the platform validates at startup. If a declared tool is not registered, the platform logs a warning.

```csharp
services.AddSingleton<IAgentToolsSeed>(new AgentToolsSeed(
    agentName: "financial-analyst",
    toolNames: ["stock_quote", "stock_history", "edgar_company_facts"]));
```

The interface:

```csharp
public interface IAgentToolsSeed
{
    string AgentName { get; }
    IReadOnlyList<string> ToolNames { get; }
}
```

This is a declaration, not a restriction. The platform uses it for startup validation and tool routing. The agent can still use platform-provided tools (context management, file operations, etc.) that are registered globally.

### 4. Context Seeds (Optional)

Beyond the persona, you can seed additional context domains. Each domain has its own seed interface, but they all follow the same pattern: implement the interface, register it as a singleton, and the platform persists it on first startup.

**Guardrail seed** -- policy rules the LLM must follow:

```csharp
services.AddSingleton<IGuardrailSeed>(new GuardrailSeed(
    name: "investment-disclaimer",
    severity: "high",
    enabled: true,
    rule: """
        Never provide specific buy, sell, or hold recommendations.
        Always include a disclaimer that the analysis is informational only
        and not investment advice.
        Do not predict future stock prices or guarantee returns.
        """));
```

**Context seed** -- background knowledge injected into the system prompt:

```csharp
services.AddSingleton<IContextSeed>(new ContextSeed(
    topic: "financial-analysis-methodology",
    tags: "finance,methodology",
    type: "fact",
    content: """
        ## Financial Analysis Reference
        - **P/E Ratio**: Price / Earnings per Share. Compare within sector.
        - **Revenue Growth**: Year-over-year revenue change.
        - **EPS**: Earnings per Share. Primary driver of stock valuation.
        """));
```

The full set of optional seed interfaces:

| Interface | Purpose |
|-----------|---------|
| `IGuardrailSeed` | Behavioral constraints with severity levels |
| `IContextSeed` | Typed knowledge (fact, reference, decision, feedback) |
| `IWorkflowSeed` | Multi-step workflow definitions |
| `IEntitySeed` | Domain entity schemas |
| `ISkillSeed` | Reusable prompt templates |
| `IUserProfileSeed` | User role, timezone, and preferences |
| `IMcpConnectionSeed` | MCP servers to auto-connect at startup |

You can register multiple seeds of each type. Each one becomes a markdown file in the agent's data directory.

### 5. DI Registration

All seeds and tool registrations go into a single extension method. This is the agent's entire public API -- one method that wires everything into the service collection.

```csharp
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Agent.FinancialAnalyst;

public static class FinancialAnalystServiceExtensions
{
    public static IServiceCollection AddFinancialAnalystAgent(this IServiceCollection services)
    {
        // Persona
        services.AddSingleton<IPersonaSeed>(new PersonaSeed(
            name: "financial-analyst",
            description: "A financial analyst that uses market data tools",
            instructions: "..."));

        // Context, guardrails, workflows, entities, skills, profile...
        services.AddSingleton<IContextSeed>(new ContextSeed(...));
        services.AddSingleton<IGuardrailSeed>(new GuardrailSeed(...));
        services.AddSingleton<IWorkflowSeed>(new WorkflowSeed(...));
        services.AddSingleton<IEntitySeed>(new EntitySeed(...));
        services.AddSingleton<ISkillSeed>(new SkillSeed(...));
        services.AddSingleton<IUserProfileSeed>(new UserProfileSeed(...));

        // Tool binding
        services.AddSingleton<IAgentToolsSeed>(new AgentToolsSeed(
            agentName: "financial-analyst",
            toolNames: ["stock_quote", "stock_history", "edgar_company_facts"]));

        return services;
    }
}
```

The agent project only needs two dependencies: `AiAgentCanvas.Abstractions` (for the seed interfaces) and `Microsoft.Extensions.DependencyInjection.Abstractions` (for `IServiceCollection`). No AI framework references needed -- the agent does not own tools or the LLM client.

### 6. Composition Root

One line in `Program.cs` activates the agent:

```csharp
builder.Services.AddFinancialAnalystAgent();
```

That's it. The platform resolves all `IPersonaSeed`, `IContextSeed`, and other seed services at startup, persists them to disk, and makes them available to the runtime agent.

## What the Platform Provides

Everything outside those six pieces is handled by the platform:

- **HarnessAgent runtime** -- the MAF SDK's `AsHarnessAgent` creates the agent with system prompt assembly, tool routing, and conversation management
- **AG-UI streaming** -- real-time streaming of agent responses to the frontend via the Agent-User Interaction protocol
- **Context injection** -- `AIContextProvider` implementations inject personas, guardrails, entities, profiles, and context into the system prompt before every LLM call
- **Tool deduplication** -- multiple registrations of the same tool are deduplicated automatically
- **Governance wrapping** -- `GovernedAIFunction` and `GovernedMcpGateway` evaluate every tool call against active guardrails and policies
- **File workspace** -- `FileSystemAgentFileStore` gives the agent sandboxed file read/write access
- **Background agents** -- long-running tasks execute in background agent threads
- **Tool approval** -- `ToolApprovalAgentOptions` with configurable auto-approval rules
- **Telemetry** -- OpenTelemetry tracing and structured logging across the full request pipeline
- **Health checks** -- readiness and liveness probes for orchestrator health
- **A2A protocol** -- Agent-to-Agent communication for multi-agent scenarios
- **Sessions and history** -- conversation persistence and session management
