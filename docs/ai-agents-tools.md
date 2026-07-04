> [AI Agents](ai-agents.md) > AI Agents: Tools and Skills

# AI Agents: Tools and Skills

Tools are how AI agents interact with the world. Without tools, an agent can only generate text. With tools, it can query databases, call APIs, search documents, manage schedules, and perform calculations. Skills extend this concept by allowing agents to create and manage reusable prompt-based tools at runtime.

## What Are AI Tools

In the Microsoft.Extensions.AI ecosystem, a tool is represented by the `AITool` base class. The most common concrete type is `AIFunction`, which wraps a .NET method so the LLM can call it.

The LLM never executes tools directly. Instead, it receives tool **schemas** (name, description, parameter definitions) and decides when to call them. The platform handles execution and feeds results back.

### AIFunction and AIFunctionFactory

`AIFunctionFactory.Create` is the primary way to define tools in AI Agent Canvas. It takes a delegate and metadata, producing an `AITool` the platform can register:

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

The `[Description]` attribute on parameters generates the JSON schema that the LLM uses to understand what arguments to provide:

```csharp
[Description("Connect to an MCP server and register its tools")]
private async Task<string> ConnectMcpServer(
    [Description("A unique name for this connection")] string name,
    [Description("The server endpoint URL")] string endpoint,
    [Description("Transport type: 'http' or 'sse'")] string transport,
    CancellationToken ct)
{
    // Implementation
}
```

Good tool descriptions are critical. The LLM relies on them to decide **when** and **how** to call a tool. Vague descriptions lead to incorrect tool selection.

## The Tool Call Loop

When the LLM decides to use a tool, the following sequence executes:

```
User: "What MCP servers are connected?"
                    |
                    v
           +----------------+
           |   LLM Reasons  |
           | "I should call |
           | list_mcp_      |
           | connections"   |
           +-------+--------+
                   |
                   v
           +----------------+
           | Platform       |
           | executes       |
           | ListMcp...()   |
           +-------+--------+
                   |
                   v
           +----------------+
           | Result:        |
           | {"count": 2,   |
           |  "connections":.|
           |  [...]}        |
           +-------+--------+
                   |
                   v
           +----------------+
           | LLM receives   |
           | result, formats|
           | human-readable |
           | response       |
           +----------------+
```

Key points about tool execution:

1. **The LLM chooses** -- The platform never forces a tool call. The LLM infers from context and tool descriptions.
2. **The platform executes** -- Tool methods run in the server process, not in the LLM. This is important for security and data access.
3. **Results feed back** -- Tool output becomes a new message in the conversation, and the LLM generates its response with that information.
4. **Multiple rounds** -- The LLM can call multiple tools in sequence, using each result to inform the next step.

## DynamicToolRegistry

Not all tools are known at startup. AI Agent Canvas uses `DynamicToolRegistry` to compose tools from multiple sources at runtime:

```csharp
public sealed class DynamicToolRegistry
{
    private readonly ConcurrentDictionary<string, List<AITool>> _toolsBySource = new();

    public void Register(string source, IEnumerable<AITool> tools)
    {
        _toolsBySource[source] = tools.ToList();
    }

    public void Unregister(string source)
    {
        _toolsBySource.TryRemove(source, out _);
    }

    public IReadOnlyList<AITool> GetAllTools()
    {
        return _toolsBySource.Values.SelectMany(t => t).ToList();
    }
}
```

Tools are grouped by **source** -- a string key that identifies where they came from. For example:

- `"mcp:market-data"` -- Tools from a connected MCP server
- `"skills"` -- Tools loaded from skill definitions
- `"system"` -- Built-in system tools

The `DynamicToolContextProvider` injects these dynamic tools into every LLM call:

```csharp
internal sealed class DynamicToolContextProvider : AIContextProvider
{
    private readonly DynamicToolRegistry _registry;

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        var dynamicTools = _registry.GetAllTools();
        if (dynamicTools.Count > 0)
        {
            var existing = context.AIContext.Tools?.ToList() ?? [];
            existing.AddRange(dynamicTools);
            context.AIContext.Tools = existing;
        }
        return new ValueTask<AIContext>(context.AIContext);
    }
}
```

This means when an MCP server is connected at runtime, its tools immediately become available to the agent without a restart.

## Tool Providers

AI Agent Canvas organizes tools into **tool provider** classes. Each provider owns a specific domain of tools:

| Provider | Tools | Purpose |
|---|---|---|
| `McpConnectionManager` | `connect_mcp_server`, `disconnect_mcp_server`, `list_mcp_connections` | Manage MCP data connections |
| `SkillToolProvider` | `create_skill`, `list_skills`, `run_skill`, `remove_skill` | Manage reusable prompt skills |
| `SkillAuthoringToolProvider` | `author_skill`, `edit_skill`, `read_skill`, `delete_authored_skill` | Author skill markdown files |
| `SchedulerToolProvider` | Schedule-related tools | Manage scheduled agent tasks |
| `WorkflowToolProvider` | Workflow tools | Execute multi-step workflows |
| `SystemToolProvider` | System-level utilities | Core system operations |

Tool providers implement a `GetTools()` method that returns their tools as `IReadOnlyList<AITool>`. These are registered in DI and collected during agent construction.

## Skills: Agent-Managed Tools

Skills are a higher-level abstraction on top of tools. A skill is a **persisted prompt template** that the agent can create, manage, and execute at runtime.

### Creating a Skill

When a user asks the agent to "create a skill that summarizes text," the agent calls `create_skill`:

```csharp
private string CreateSkill(string name, string description, string promptTemplate)
{
    var record = new SkillRecord
    {
        Id = id,
        Name = normalizedName,
        Description = description,
        PromptTemplate = promptTemplate,  // e.g., "Summarize: {input}"
    };
    _store.SaveSkill(record);
}
```

### Running a Skill

When the skill is invoked via `run_skill`, the platform substitutes the `{input}` placeholder and sends the expanded prompt to the agent:

```csharp
private async Task<string> RunSkill(string name, string input, CancellationToken ct)
{
    var skill = _store.GetSkill(name);
    var prompt = skill.PromptTemplate.Replace("{input}", input);

    var messages = new List<ChatMessage>
    {
        new(ChatRole.User, prompt),
    };

    var response = await _agent.RunAsync(messages, cancellationToken: ct);
    return JsonSerializer.Serialize(new { skill = name, result = response.Text });
}
```

### Skill Authoring

Beyond runtime skill creation, `SkillAuthoringToolProvider` allows the agent to write skill definitions as **markdown files** with YAML frontmatter:

```yaml
---
name: summarize
description: Summarize text into key points
tags: text, summary
---

Summarize the following text into 3-5 key bullet points:

{input}
```

This persistence format makes skills portable, version-controllable, and human-readable.

## Tool Design Guidelines

When building tools for AI Agent Canvas:

1. **Descriptive names** -- Use verb-noun format: `connect_mcp_server`, not `mcp` or `connect`.
2. **Clear descriptions** -- Both the tool and each parameter need descriptions the LLM can reason about.
3. **Return structured data** -- Return JSON strings so the LLM can parse and present results cleanly.
4. **Handle errors gracefully** -- Return error information as data rather than throwing exceptions.
5. **Keep tools focused** -- One tool per action. Avoid multi-purpose tools with mode parameters.
6. **Use CancellationToken** -- Long-running tools should accept and respect cancellation tokens.

## Tools and Skills Summary

The tools and skills system in AI Agent Canvas provides a layered approach:

- **AITool / AIFunction** -- The foundation: .NET methods the LLM can call
- **Tool Providers** -- Organized groups of related tools
- **DynamicToolRegistry** -- Runtime composition of tools from multiple sources
- **Skills** -- Persisted prompt templates the agent manages as reusable capabilities

---

