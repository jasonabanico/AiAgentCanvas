> [AI Agents](ai-agents.md) > AI Agents: AG-UI Protocol

# AI Agents: AG-UI Protocol

AG-UI (Agent Graphical UI) is a protocol for streaming AI agent responses to frontend applications in real time. AI Agent Canvas uses AG-UI to connect the .NET backend (powered by Microsoft Agent Framework) to the CopilotKit frontend over Server-Sent Events (SSE).

## What Is AG-UI

Traditional REST APIs return a complete response in a single payload. For AI agents that may take seconds to generate a response, this creates a poor user experience -- the user stares at a loading spinner until the entire response is ready.

AG-UI solves this by streaming **events** as the agent processes. The user sees text appear token by token, knows when the agent starts and finishes, and gets a responsive conversational experience.

The protocol defines a set of event types that represent the lifecycle of an agent response:

```
Client POST /api/copilotkit
     |
     v
Server opens SSE stream
     |
     +-- event: run.started
     +-- event: text.message.start
     +-- event: text.message.content  (repeated, one per token chunk)
     +-- event: text.message.content
     +-- event: text.message.content
     +-- event: text.message.end
     +-- event: run.finished
     |
     v
SSE stream closes
```

## The /api/copilotkit Endpoint

AI Agent Canvas exposes a single SSE endpoint that CopilotKit connects to:

```csharp
public static class AgUiEndpoint
{
    public static void MapAgUiEndpoints(this WebApplication app)
    {
        app.MapPost("/api/copilotkit", HandleCopilotKitRequest);
    }
}
```

This endpoint is mapped in the application startup via `app.UseAiAgentCanvas()`, which internally calls `app.MapAgUiEndpoints()`.

## Request Format

The frontend sends a POST request with a JSON body containing the conversation messages and an optional thread ID:

```json
{
  "threadId": "conv-abc-123",
  "messages": [
    {
      "role": "system",
      "content": "You are a helpful assistant."
    },
    {
      "role": "user",
      "content": "What is the current market summary?"
    }
  ]
}
```

The endpoint extracts messages and initializes the agent session:

```csharp
var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);
var messages = body.GetProperty("messages");
var threadId = body.TryGetProperty("threadId", out var tid)
    ? tid.GetString() ?? Guid.NewGuid().ToString()
    : Guid.NewGuid().ToString();
var runId = Guid.NewGuid().ToString();
```

Each request gets a unique `runId`. The `threadId` persists across messages in the same conversation, enabling continuity.

## SSE Response Format

The response uses the `text/event-stream` content type with standard SSE formatting:

```csharp
context.Response.ContentType = "text/event-stream";
context.Response.Headers.CacheControl = "no-cache";
context.Response.Headers.Connection = "keep-alive";
```

Each event is written as:

```
event: <event-type>
data: <json-payload>
```

Note the double newline after the data line -- this is the SSE event delimiter.

## Event Types

### run.started

Signals that the agent has begun processing the request:

```
event: run.started
data: {"threadId":"conv-abc-123","runId":"run-xyz-789"}
```

The frontend uses this to show a processing indicator and track the run.

### text.message.start

Marks the beginning of a new assistant message:

```
event: text.message.start
data: {"messageId":"msg-001","role":"assistant","agentName":"AiAgentCanvas"}
```

CopilotKit creates a new message bubble in the UI when it receives this event.

### text.message.content

Delivers a chunk of the response text. This event is sent repeatedly as tokens stream from the LLM:

```
event: text.message.content
data: {"messageId":"msg-001","delta":"The current"}

event: text.message.content
data: {"messageId":"msg-001","delta":" market shows"}

event: text.message.content
data: {"messageId":"msg-001","delta":" strong momentum..."}
```

The `delta` field contains the incremental text. The frontend appends each delta to build the complete message progressively.

### text.message.end

Signals that the message is complete:

```
event: text.message.end
data: {"messageId":"msg-001"}
```

### run.finished

Signals that the entire agent run is complete:

```
event: run.finished
data: {"threadId":"conv-abc-123","runId":"run-xyz-789"}
```

The frontend uses this to stop the processing indicator and re-enable the input field.

## Streaming Implementation

The backend iterates over the agent's streaming output and emits SSE events for each chunk:

```csharp
await foreach (var update in agent.RunStreamingAsync(
    chatMessages, session, cancellationToken: context.RequestAborted))
{
    if (update.Text is { Length: > 0 } text)
    {
        await WriteEvent(context, "text.message.content",
            new { messageId, delta = text });
    }
}
```

The `WriteEvent` helper formats and flushes each event:

```csharp
private static async Task WriteEvent(
    HttpContext context, string eventType, object data)
{
    var json = JsonSerializer.Serialize(data, JsonOptions);
    await context.Response.WriteAsync(
        $"event: {eventType}\ndata: {json}\n\n",
        context.RequestAborted);
    await context.Response.Body.FlushAsync(context.RequestAborted);
}
```

The `FlushAsync` call is critical -- without it, the response may be buffered and the client would not receive events in real time.

## How CopilotKit Consumes the Stream

On the frontend, CopilotKit handles the SSE stream automatically. The Next.js application configures CopilotKit with the backend URL:

```jsx
<CopilotKit runtimeUrl="/api/copilotkit">
  <CopilotChat />
</CopilotKit>
```

CopilotKit:

1. Sends POST requests to `/api/copilotkit` when the user submits a message
2. Opens an SSE connection on the response
3. Parses incoming events by type
4. Updates the chat UI progressively as `text.message.content` events arrive
5. Manages conversation state across multiple request/response cycles

## Why SSE Over WebSockets

AI Agent Canvas uses SSE rather than WebSockets for several reasons:

| Consideration | SSE | WebSockets |
|---|---|---|
| Direction | Server-to-client (unidirectional) | Bidirectional |
| HTTP compatibility | Standard HTTP, works with all proxies/CDNs | Requires protocol upgrade |
| Reconnection | Built-in automatic reconnect | Must implement manually |
| Complexity | Simple text protocol | Binary framing, connection management |
| Load balancers | Works with standard HTTP load balancers | Requires sticky sessions or special config |
| Firewall friendliness | Standard HTTPS port, no special rules | May be blocked by corporate firewalls |

For the agent use case, communication is fundamentally **request-response with streaming**: the client sends a message (POST), and the server streams back the response. There is no need for the server to push unsolicited messages or for the client to send data mid-stream.

SSE fits this pattern naturally:

- The POST request carries the user message
- The SSE response carries the streaming agent response
- HTTP infrastructure (proxies, CDNs, load balancers) works without modification
- Automatic reconnection handles network interruptions

WebSockets would add complexity (connection management, heartbeats, reconnection logic) without providing benefits for this use case.

## Cancellation

The endpoint respects the HTTP request's cancellation token:

```csharp
cancellationToken: context.RequestAborted
```

If the client closes the connection (user navigates away, network drops), the `RequestAborted` token fires, canceling the agent's LLM call and stopping the stream. This prevents wasted compute on responses nobody will read.

## AG-UI Summary

The AG-UI protocol provides a clean boundary between the .NET agent backend and the CopilotKit frontend:

- **POST /api/copilotkit** -- Single endpoint for all agent interactions
- **SSE events** -- `run.started`, `text.message.start`, `text.message.content`, `text.message.end`, `run.finished`
- **Progressive rendering** -- Users see responses as they are generated, token by token
- **Standard HTTP** -- No special infrastructure required, works with existing proxies and load balancers
- **Cancellation** -- Clean shutdown when clients disconnect

---

> **[Download the complete PDF guide](guides/AI-Agent-Canvas-Guide.pdf)** | **[AI-First Company Guide](guides/AI-First-Company-Guide.pdf)**
