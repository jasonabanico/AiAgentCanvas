> [Developer Guide](developer-guide.md) > Skills & MCP

# Developer Guide: Skills & MCP

The `AiAgentCanvas.Skills` project manages skill persistence, external MCP server connections, local skill resolution, and skill authoring. It enables the agent to connect to arbitrary MCP servers at runtime and register their tools dynamically.

## Overview

The skills system has four components, each with its own registration method:

| Component | Registration | Purpose |
|-----------|-------------|---------|
| `SkillStore` + `SkillToolProvider` | `AddAiAgentCanvasSkills()` | SQLite-backed skill persistence and management |
| `McpConnectionManager` | `AddAiAgentCanvasMcp()` | Connect/disconnect external MCP servers at runtime |
| `LocalSkillRegistry` | `AddAiAgentCanvasSkillRegistry()` | Resolve skills from local markdown files |
| `SkillAuthoringToolProvider` | `AddAiAgentCanvasSkillAuthoring()` | Create and edit skills via chat |

```csharp
builder.Services.AddAiAgentCanvasSkills();
builder.Services.AddAiAgentCanvasMcp();
builder.Services.AddAiAgentCanvasSkillRegistry();
builder.Services.AddAiAgentCanvasSkillAuthoring();
```

## SkillStore

The `SkillStore` persists skill definitions in a SQLite database. It provides CRUD operations for skill records.

```csharp
public sealed class SkillStore
{
    public SkillStore(string connectionString = "Data Source=skills.db") { ... }
}
```

The `SkillToolProvider` exposes these operations as AITools so the LLM can manage skills through conversation.

### Registration

```csharp
public static IServiceCollection AddAiAgentCanvasSkills(
    this IServiceCollection services,
    string connectionString = "Data Source=skills.db")
{
    services.AddSingleton(new SkillStore(connectionString));
    services.AddSingleton<SkillToolProvider>();
    services.AddSingleton<IReadOnlyList<AITool>>(sp =>
        sp.GetRequiredService<SkillToolProvider>().GetTools());
    return services;
}
```

## McpConnectionManager

The `McpConnectionManager` is the runtime MCP client. It allows the agent to connect to external MCP servers, discover their tools, and register those tools into the `DynamicToolRegistry` so the LLM can use them.

### How It Works

1. The LLM calls the `connect_mcp_server` tool with a name, endpoint URL, and transport type
2. `McpConnectionManager` creates an `HttpClientTransport` and connects via `McpClient.CreateAsync()`
3. It calls `ListToolsAsync()` to discover the server's tools
4. The discovered tools are registered into `DynamicToolRegistry` under the key `mcp:{name}`
5. `DynamicToolContextProvider` picks up the new tools on the next LLM invocation

### Exposed Tools

The manager itself exposes three management tools:

```csharp
public IReadOnlyList<AITool> GetTools()
{
    return
    [
        AIFunctionFactory.Create(ConnectMcpServer, "connect_mcp_server",
            "Connect to an MCP server and register its tools"),
        AIFunctionFactory.Create(DisconnectMcpServer, "disconnect_mcp_server",
            "Disconnect from an MCP server and remove its tools"),
        AIFunctionFactory.Create(ListMcpConnections, "list_mcp_connections",
            "List all active MCP server connections"),
    ];
}
```

### connect_mcp_server

Connects to an MCP server and registers its tools:

```csharp
private async Task<string> ConnectMcpServer(string name, string endpoint,
    string transport, CancellationToken ct)
{
    IClientTransport clientTransport = transport.ToLowerInvariant() switch
    {
        "http" or "sse" => new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(endpoint) }),
        _ => throw new ArgumentException($"Unsupported transport: {transport}"),
    };

    var client = await McpClient.CreateAsync(clientTransport, cancellationToken: ct);
    var mcpTools = await client.ListToolsAsync(cancellationToken: ct);
    var aiTools = mcpTools.Cast<AITool>().ToList();

    _connections[name] = new McpConnection { /* ... */ };
    _registry.Register($"mcp:{name}", aiTools);

    return JsonSerializer.Serialize(new {
        status = "connected", name, endpoint,
        toolCount = aiTools.Count,
        tools = aiTools.Select(t => t.Name).ToList()
    });
}
```

### disconnect_mcp_server

Removes a connection and unregisters its tools from the `DynamicToolRegistry`:

```csharp
private async Task<string> DisconnectMcpServer(string name, CancellationToken ct)
{
    if (!_connections.TryRemove(name, out var connection))
        return JsonSerializer.Serialize(new { error = $"Connection '{name}' not found" });

    _registry.Unregister($"mcp:{name}");
    if (connection.Client is not null)
        await connection.Client.DisposeAsync();

    return JsonSerializer.Serialize(new { status = "disconnected", name });
}
```

### list_mcp_connections

Returns all active connections with their tool counts:

```csharp
private string ListMcpConnections()
{
    var connections = _connections.Values.Select(c => new
    {
        c.Name, c.Endpoint, c.Transport,
        toolCount = c.Tools?.Count ?? 0,
    }).ToList();

    return JsonSerializer.Serialize(new { count = connections.Count, connections });
}
```

### Registration

```csharp
public static IServiceCollection AddAiAgentCanvasMcp(this IServiceCollection services)
{
    services.AddSingleton<McpConnectionManager>();
    services.AddSingleton<IReadOnlyList<AITool>>(sp =>
        sp.GetRequiredService<McpConnectionManager>().GetTools());
    return services;
}
```

## LocalSkillRegistry

The `LocalSkillRegistry` resolves skills from local markdown files at runtime. It combines data from the `SkillStore`, `McpConnectionManager`, and skill files on disk.

```csharp
public static IServiceCollection AddAiAgentCanvasSkillRegistry(
    this IServiceCollection services,
    string skillsDirectory = "./agent-data/skills")
{
    services.AddSingleton(sp => new LocalSkillRegistry(
        skillsDirectory,
        sp.GetRequiredService<SkillStore>(),
        sp.GetRequiredService<McpConnectionManager>(),
        sp.GetRequiredService<ILogger<LocalSkillRegistry>>()));
    services.AddSingleton<IReadOnlyList<AITool>>(sp =>
        sp.GetRequiredService<LocalSkillRegistry>().GetTools());
    return services;
}
```

## SkillAuthoringToolProvider

Provides tools for creating and editing skill definitions through conversation. Skills are stored as markdown files in the skills directory.

```csharp
public static IServiceCollection AddAiAgentCanvasSkillAuthoring(
    this IServiceCollection services,
    string skillsDirectory = "./agent-data/skills")
{
    services.AddSingleton(sp => new SkillAuthoringToolProvider(
        skillsDirectory,
        sp.GetRequiredService<ILogger<SkillAuthoringToolProvider>>()));
    services.AddSingleton<IReadOnlyList<AITool>>(sp =>
        sp.GetRequiredService<SkillAuthoringToolProvider>().GetTools());
    return services;
}
```

## MCP Connection Lifecycle

```
User: "Connect to my data server at https://data.example.com/mcp"
    |
    v
LLM calls connect_mcp_server(name="data", endpoint="https://...", transport="http")
    |
    v
McpConnectionManager:
  1. Creates HttpClientTransport
  2. Connects via McpClient.CreateAsync()
  3. Lists tools via ListToolsAsync()
  4. Registers tools: _registry.Register("mcp:data", aiTools)
    |
    v
Next LLM invocation:
  DynamicToolContextProvider reads DynamicToolRegistry
  -> MCP server's tools appear in ChatOptions.Tools
  -> LLM can now call them
    |
    v
User: "Disconnect from the data server"
    |
    v
LLM calls disconnect_mcp_server(name="data")
    |
    v
McpConnectionManager:
  1. _registry.Unregister("mcp:data")
  2. Disposes McpClient
  -> Tools removed from future invocations
```

## Security Integration

MCP connections are subject to governance policies. The `GovernedMcpGateway` in the Security project can block connections to private/internal addresses (SSRF protection) and require approval for specific tools. See the [Security](#security) section for details on the governance policy rules that apply to `connect_mcp_server`.

---

## Adding Custom MCP Connections

Custom MCP connections in AI Agent Canvas are projects that provide tools (data connections, API integrations) to the agent. Each connection is a class library that defines tools using `AIFunctionFactory.Create()` and registers them as `IReadOnlyList<AITool>` services.

The term "MCP connection" here refers to a local tool provider project (not an external MCP server -- for connecting to remote MCP servers at runtime, see [Skills & MCP](#skills--mcp)).

### How Tool Registration Works

The Core platform collects all `IReadOnlyList<AITool>` singleton services at startup and passes them to the `ChatClientAgent`:

```csharp
// Inside AddAiAgentCanvas() in Core:
var tools = sp.GetServices<IReadOnlyList<AITool>>().SelectMany(t => t).ToList();
var agentOptions = new ChatClientAgentOptions
{
    ChatOptions = new ChatOptions { Tools = tools },
};
```

Your custom project just needs to register its tools as `IReadOnlyList<AITool>` and they automatically become available to the LLM.

### Step-by-Step Guide (MCP)

#### Step 1: Create the Project

Create a new folder under `src/Custom/` and a `.csproj` with the `Microsoft.Extensions.AI` package:

```
src/Custom/MCP.Weather/
```

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.AI" Version="10.7.0" />
  </ItemGroup>

</Project>
```

Add `Microsoft.Extensions.Http.Resilience` if you need HTTP clients with retry policies.

#### Step 2: Create a ToolProvider Class

Define your tools using `AIFunctionFactory.Create()`. Each tool is a method decorated with `[Description]` attributes:

```csharp
using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MCP.Weather;

public sealed class WeatherToolProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;
    private readonly ILogger<WeatherToolProvider> _logger;

    public WeatherToolProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<WeatherToolProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = configuration["Weather:ApiKey"] ?? "";
        _logger = logger;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(GetCurrentWeather, "get_current_weather",
                "Get current weather conditions for a city"),
            AIFunctionFactory.Create(GetForecast, "get_weather_forecast",
                "Get a 5-day weather forecast for a city"),
        ];
    }

    [Description("Get current weather conditions for a city")]
    private async Task<string> GetCurrentWeather(
        [Description("City name (e.g. 'Seattle', 'London')")] string city,
        CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Weather");
            var url = $"https://api.weather.example.com/current?q={city}&key={_apiKey}";
            var result = await client.GetStringAsync(url, ct);

            _logger.LogInformation("Weather fetched for {City}", city);
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Weather fetch failed for {City}", city);
            return JsonSerializer.Serialize(new { error = $"Failed: {ex.Message}" });
        }
    }

    [Description("Get a 5-day weather forecast for a city")]
    private async Task<string> GetForecast(
        [Description("City name")] string city,
        [Description("Number of days (1-5)")] int days,
        CancellationToken ct)
    {
        // Implementation similar to above
        return "...";
    }
}
```

Key points:

- The constructor receives dependencies from DI (IHttpClientFactory, IConfiguration, ILogger)
- `GetTools()` returns the tool list using `AIFunctionFactory.Create()`
- Each tool method uses `[Description]` attributes so the LLM understands parameters
- Tools return JSON strings (the LLM parses the response)
- Always handle errors gracefully and return error JSON instead of throwing

#### Step 3: Create ServiceExtensions

Create an extension method that registers the HttpClient, the tool provider, and the tools:

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace MCP.Weather;

public static class WeatherServiceExtensions
{
    public static IServiceCollection AddWeatherTools(this IServiceCollection services)
    {
        services.AddHttpClient("Weather", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .AddStandardResilienceHandler();

        services.AddSingleton<WeatherToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<WeatherToolProvider>().GetTools());

        return services;
    }
}
```

The critical line is the `IReadOnlyList<AITool>` registration. This is how the Core platform discovers your tools.

#### Step 4: Wire Up in Program.cs

Add the project reference and call your extension method:

```xml
<!-- In AiAgentCanvas.Web.csproj -->
<ItemGroup>
  <ProjectReference Include="..\Custom\MCP.Weather\MCP.Weather.csproj" />
</ItemGroup>
```

```csharp
// In Program.cs
using MCP.Weather;

builder.Services.AddWeatherTools();
```

Add the project to the solution:

```
dotnet sln AiAgentCanvas.sln add src/Custom/MCP.Weather/MCP.Weather.csproj --solution-folder Custom
```

### Full Reference: MCP.HelloWorldData

The `MCP.HelloWorldData` project in `src/Custom/MCP.HelloWorldData/` is the included sample implementation. It provides three tools for stock market data.

#### MarketDataToolProvider.cs

The tool provider defines three tools:

```csharp
public IReadOnlyList<AITool> GetTools()
{
    return
    [
        AIFunctionFactory.Create(EdgarCompanyFactsAsync, "edgar_company_facts",
            "Fetch SEC EDGAR financial data for a company by ticker symbol"),
        AIFunctionFactory.Create(StockQuoteAsync, "stock_quote",
            "Get current stock price from Yahoo Finance"),
        AIFunctionFactory.Create(StockHistoryAsync, "stock_history",
            "Get historical stock data from Yahoo Finance with configurable range"),
    ];
}
```

Each tool:

- Takes typed parameters with `[Description]` attributes
- Uses `IHttpClientFactory` to make HTTP requests
- Returns JSON strings with structured data or error information
- Logs timing and errors via `ILogger`
- Accepts `CancellationToken` for proper cancellation

#### MarketDataServiceExtensions.cs

```csharp
public static IServiceCollection AddMarketDataTools(this IServiceCollection services)
{
    services.AddHttpClient("SEC", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "AiAgentCanvas/1.0 (contact@example.com)");
        })
        .AddStandardResilienceHandler();

    services.AddHttpClient("YahooFinance", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        })
        .AddStandardResilienceHandler();

    services.AddSingleton<MarketDataToolProvider>();
    services.AddSingleton<IReadOnlyList<AITool>>(sp =>
        sp.GetRequiredService<MarketDataToolProvider>().GetTools());

    return services;
}
```

Notable patterns:

- Named HttpClients for different API backends
- `AddStandardResilienceHandler()` for automatic retry and circuit-breaking
- Custom User-Agent header for the SEC EDGAR API (which requires identification)
- Separate timeout values per API

### Testing Tools

Once registered, your tools are automatically discovered by the agent. To test:

1. Build: `dotnet build AiAgentCanvas.sln`
2. Run: `cd src/AiAgentCanvas.Web && dotnet run`
3. Open the CopilotKit frontend
4. Ask the agent to use your tool (e.g., "What's the weather in Seattle?")
5. Check the backend console for tool call logs

The LLM discovers tools from their names and descriptions. Make sure your tool names are descriptive and your `[Description]` attributes clearly explain what each parameter expects.

### Tool Design Guidelines

1. **Return JSON strings** -- The LLM parses your tool's return value. Always return structured JSON.
2. **Handle errors gracefully** -- Return `{ "error": "..." }` instead of throwing exceptions.
3. **Use CancellationToken** -- Accept and pass through the cancellation token for proper request cancellation.
4. **Log appropriately** -- Use `ILogger` for timing, errors, and debugging.
5. **Keep tools focused** -- One tool per API operation. Let the LLM compose multiple tool calls.
6. **Descriptive names** -- Use snake_case names that clearly describe the action (e.g., `get_current_weather`, not `weather`).
7. **Document parameters** -- Every parameter needs a `[Description]` attribute with examples.

### Tools Without External APIs

Not every tool needs HTTP calls. You can create tools that do local computation, file operations, or database queries:

```csharp
public IReadOnlyList<AITool> GetTools()
{
    return
    [
        AIFunctionFactory.Create(CalculateCompoundInterest, "calculate_compound_interest",
            "Calculate compound interest over a period"),
    ];
}

[Description("Calculate compound interest")]
private string CalculateCompoundInterest(
    [Description("Principal amount")] decimal principal,
    [Description("Annual interest rate (e.g. 0.05 for 5%)")] decimal rate,
    [Description("Number of years")] int years)
{
    var result = principal * (decimal)Math.Pow((double)(1 + rate), years);
    return JsonSerializer.Serialize(new { principal, rate, years, futureValue = result });
}
```

These tools follow the same registration pattern and are automatically available to the agent.

---

