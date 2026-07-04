> [AI Agents](ai-agents.md) > AI Agents: Microsoft Agent Framework

# AI Agents: Microsoft Agent Framework

Microsoft Agent Framework (MAF) is the orchestration layer that powers AI Agent Canvas. It provides the abstractions for building agents that can reason, call tools, and stream responses -- all while integrating with the broader Microsoft.Extensions.AI ecosystem.

AI Agent Canvas uses MAF version **1.10.0** from the `Microsoft.Agents.AI` namespace.

## Core Types

### IChatClient

At the foundation, MAF builds on `IChatClient` from `Microsoft.Extensions.AI`. This interface represents any LLM provider -- Azure AI Foundry, OpenAI, Ollama, or others. AI Agent Canvas configures it through `AIFoundryClientFactory`:

```csharp
services.AddChatClient(sp =>
    sp.GetRequiredService<AIFoundryClientFactory>().CreateChatClient());
```

This abstraction means the platform is not locked to a specific LLM provider. Swapping from Azure AI Foundry to another provider requires only changing the `IChatClient` registration.

### ChatClientAgent

`ChatClientAgent` is the primary agent implementation in MAF. It wraps an `IChatClient` and adds the agent execution loop: building messages, attaching tools, calling the LLM, handling tool calls, and streaming responses.

```csharp
var agent = new ChatClientAgent(chatClient, agentOptions, loggerFactory, serviceProvider);
```

The `ChatClientAgent` constructor takes four parameters:

| Parameter | Purpose |
|---|---|
| `IChatClient` | The LLM provider to use for inference |
| `ChatClientAgentOptions` | Configuration: name, tools, context providers |
| `ILoggerFactory` | Structured logging for the agent pipeline |
| `IServiceProvider` | DI container for resolving dependencies |

### ChatClientAgentOptions

This options class configures how the agent behaves:

```csharp
var agentOptions = new ChatClientAgentOptions
{
    Name = "AiAgentCanvas",
    Description = "A multi-tool AI assistant",
    ChatOptions = new ChatOptions { Tools = tools },
    ChatHistoryProvider = chatHistoryProvider,
    AIContextProviders = contextProviders,
};
```

Key properties:

- **Name / Description** -- Identify the agent in logs and multi-agent scenarios
- **ChatOptions** -- Wraps `ChatOptions` from Microsoft.Extensions.AI, including the tool list
- **ChatHistoryProvider** -- Optional provider for loading/saving conversation history
- **AIContextProviders** -- Chain of providers that inject instructions, tools, and context before each LLM call

### AIAgent

`AIAgent` is the abstract base class for all agents in MAF. `ChatClientAgent` inherits from it. The `AIAgent` type is what gets registered in DI and consumed by endpoints:

```csharp
services.AddSingleton(sp =>
{
    // ... configure ChatClientAgent ...
    return (AIAgent)builder.Build(sp);
});
```

### AgentSession

Each conversation creates an `AgentSession` that carries per-request state:

```csharp
var session = await agent.CreateSessionAsync(cancellationToken);
session.StateBag.SetValue("conversationId", threadId);
```

The `StateBag` is a key-value store for session-scoped data. Context providers and middleware can read from and write to it during the agent loop.

## The Agent Execution Model

When the agent receives a request, MAF executes the following pipeline:

```
User Message
     |
     v
+--------------------+
| AIContextProviders |  <-- Inject system prompt, RAG, persona, guardrails
| (chain)            |
+--------------------+
     |
     v
+--------------------+
| Build Messages     |  <-- Conversation history + user message + context
+--------------------+
     |
     v
+--------------------+
| Attach Tools       |  <-- Static tools + dynamic tools from registry
+--------------------+
     |
     v
+--------------------+
| Call LLM           |  <-- IChatClient.GetStreamingChatCompletionAsync
+--------------------+
     |
     +------+------+
     |             |
     v             v
  Text          Tool Call
  Response      +------------------+
  (stream)      | Execute Tool     |
                +------------------+
                     |
                     v
                +------------------+
                | Feed Result Back |  <-- Loop to "Call LLM" with tool result
                +------------------+
```

### Step 1: Context Injection

Before the LLM is called, MAF iterates through the `AIContextProviders` chain. Each provider can modify the `AIContext` -- adding instructions, appending tools, or injecting retrieved documents. Providers execute in registration order.

### Step 2: Message Construction

The framework combines conversation history (from `ChatHistoryProvider`), the current user message, and the system instructions assembled by context providers into a single message list.

### Step 3: Tool Attachment

Tools registered in `ChatOptions` and any dynamic tools added by `DynamicToolContextProvider` are attached to the LLM request. The LLM receives the tool schemas (name, description, parameters) so it can decide when to call them.

### Step 4: LLM Invocation

MAF calls `IChatClient` with the messages and tools. For streaming, it uses `GetStreamingChatCompletionAsync`, yielding tokens as they arrive.

### Step 5: Tool Call Handling

If the LLM returns a tool call instead of (or alongside) text, MAF:

1. Parses the tool name and arguments from the LLM response
2. Finds the matching `AITool` in the registered set
3. Executes the tool with the provided arguments
4. Appends the tool result as a new message
5. Calls the LLM again with the updated message list

This loop continues until the LLM produces a final text response without further tool calls.

## Middleware Pipeline

MAF supports middleware that wraps the agent execution. AI Agent Canvas uses this for logging:

```csharp
var builder = agent.AsBuilder();
builder.Use(async (messages, session, runOptions, nextAsync, ct) =>
{
    logger.LogInformation("Agent invoked. Messages={Count}", messages.Count());
    await nextAsync(messages, session, runOptions, ct);
    logger.LogInformation("Agent completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
});
```

Middleware receives the full pipeline context and calls `nextAsync` to continue execution. This pattern is familiar from ASP.NET Core middleware and enables cross-cutting concerns like:

- **Logging** -- Timing and message counts
- **Metrics** -- Token usage and tool call counts
- **Error handling** -- Catching and recovering from LLM failures
- **Caching** -- Short-circuiting repeated queries

## Running the Agent

The agent exposes two execution methods:

- **`RunAsync`** -- Returns the complete response after all tool calls resolve
- **`RunStreamingAsync`** -- Yields response chunks as an `IAsyncEnumerable`, enabling SSE streaming

AI Agent Canvas uses `RunStreamingAsync` in the AG-UI endpoint:

```csharp
await foreach (var update in agent.RunStreamingAsync(
    chatMessages, session, cancellationToken: ct))
{
    if (update.Text is { Length: > 0 } text)
    {
        await WriteEvent(context, "text.message.content",
            new { messageId, delta = text });
    }
}
```

## MAF Summary

MAF provides the execution backbone for AI Agent Canvas:

| Concern | MAF Type |
|---|---|
| LLM abstraction | `IChatClient` |
| Agent orchestration | `ChatClientAgent` / `AIAgent` |
| Configuration | `ChatClientAgentOptions` |
| Context injection | `AIContextProvider` chain |
| Session state | `AgentSession` / `StateBag` |
| Extensibility | Middleware pipeline via `AsBuilder()` |

The framework handles the complexity of the agent loop -- tool call resolution, message management, streaming -- so that application code focuses on defining tools, context providers, and business logic.

---

