# Reference: AG-UI Protocol

The AG-UI (Agent-User Interface) protocol defines how the frontend communicates with the agent backend. AI Agent Canvas implements this protocol using server-sent events (SSE) over a single HTTP POST endpoint.

## Endpoint

```
POST /api/copilotkit
Content-Type: application/json
```

The endpoint is mapped in `CoreServiceExtensions.UseAiAgentCanvas()` via `MapAGUIServer()` from the `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` package.

## Request Format

```json
{
  "threadId": "thread_abc123",
  "runId": "run_xyz789",
  "messages": [
    {
      "role": "user",
      "content": "What is the stock price of MSFT?"
    }
  ]
}
```

- `threadId` -- identifies the conversation thread. Reuse across messages to maintain history.
- `runId` -- unique identifier for this specific run.
- `messages` -- array of message objects with `role` and `content`.

## Response Format

The response is an SSE stream:

```
Content-Type: text/event-stream
Cache-Control: no-cache
Connection: keep-alive
```

Each event is a JSON object on a `data:` line, with a `type` field indicating the event type.

## Event Types

### Text Streaming

Text messages are streamed in three phases:

```
data: {"type": "TEXT_MESSAGE_START", "messageId": "msg_001"}

data: {"type": "TEXT_MESSAGE_CONTENT", "messageId": "msg_001", "delta": "The current"}

data: {"type": "TEXT_MESSAGE_CONTENT", "messageId": "msg_001", "delta": " stock price of MSFT is $425.30."}

data: {"type": "TEXT_MESSAGE_END", "messageId": "msg_001"}
```

### Tool Execution Lifecycle

Tool calls are bracketed by start and end events:

```
data: {"type": "TOOL_CALL_START", "toolCallId": "tc_001", "toolName": "stock_quote", "args": "{\"symbol\": \"MSFT\"}"}

data: {"type": "TOOL_CALL_END", "toolCallId": "tc_001", "result": "{\"symbol\": \"MSFT\", \"price\": 425.30}"}
```

### State Updates

State events push structured data to the frontend state panel. Two behaviors exist:

**Snapshot** replaces the entire state panel content:

```
data: {"type": "STATE_SNAPSHOT", "snapshot": {"symbol": "MSFT", "price": 425.30, "change": "+1.2%"}}
```

**Delta** appends to or merges with the existing state panel content:

```
data: {"type": "STATE_DELTA", "delta": {"latestAlert": "Price crossed $425 threshold"}}
```

### Agent Step Tracking

Steps track higher-level phases of agent execution:

```
data: {"type": "STEP_STARTED", "stepName": "Analyzing market data"}

data: {"type": "STEP_FINISHED", "stepName": "Analyzing market data"}
```

### Chain-of-Thought Reasoning

Reasoning messages expose the model's thought process:

```
data: {"type": "REASONING_START"}

data: {"type": "REASONING_MESSAGE_CONTENT", "delta": "I need to look up the current price..."}

data: {"type": "REASONING_END"}
```

The frontend displays reasoning blocks temporarily and clears them after 5 seconds.

### Run Lifecycle

```
data: {"type": "RUN_STARTED", "runId": "run_xyz789"}

data: {"type": "RUN_FINISHED", "runId": "run_xyz789", "outcome": "completed"}
```

When `outcome` is `"interrupt"`, the frontend shows approve/deny buttons for the user to continue or cancel the interrupted action.

### Error Reporting

```
data: {"type": "RUN_ERROR", "runId": "run_xyz789", "error": "Rate limit exceeded. Retry after 60 seconds."}
```

## Why SSE over WebSockets

The AG-UI protocol uses server-sent events rather than WebSockets for several reasons:

- **Unidirectional fit** -- the agent streams output to the client; the client sends new requests as separate HTTP POSTs. This matches the SSE model naturally.
- **HTTP compatibility** -- SSE runs over standard HTTP, so it works with existing load balancers, reverse proxies, and CDNs without special configuration.
- **Auto-reconnection** -- the browser's `EventSource` API reconnects automatically on connection loss. The frontend fetch-based SSE reader handles this as well.
- **Simpler infrastructure** -- no WebSocket upgrade handshake, no connection state to manage on the server, no sticky sessions required.
- **Firewall friendly** -- SSE uses standard HTTP ports and protocols, avoiding firewall rules that block WebSocket upgrades.

## Cancellation

The backend respects `HttpContext.RequestAborted` as a `CancellationToken`. When the client disconnects (navigates away, closes the tab, or explicitly aborts the fetch), the token fires and the agent stops processing. This prevents wasted compute on abandoned requests.

## ToolStateMapping

The `ToolStateMapping` record and `ToolStateBehavior` enum control how tool results appear in the state panel:

```csharp
public enum ToolStateBehavior
{
    Snapshot,   // Replaces the entire state panel
    Delta,      // Appends to existing state
}

public sealed record ToolStateMapping(string ToolName, ToolStateBehavior Behavior);
```

Tool state mappings are registered in each capability's service extensions. When the AG-UI stream encounters a result from a mapped tool, `AGUIStreamOptions.MapResult` routes it through either `ToStateSnapshot` or `ToStateDelta`, which emit the corresponding SSE event.

**Registered mappings:**

| Tool Name | Behavior | Source |
|-----------|----------|--------|
| `list_workflows` | Snapshot | WorkflowServiceExtensions |
| `run_workflow` | Delta | WorkflowServiceExtensions |
| `run_sequential_workflow` | Delta | WorkflowServiceExtensions |
| `run_concurrent_workflow` | Delta | WorkflowServiceExtensions |
| `list_scheduled_tasks` | Snapshot | SchedulerServiceExtensions |
| `stock_quote` | Snapshot | MarketDataServiceExtensions |
| `stock_history` | Snapshot | MarketDataServiceExtensions |
| `edgar_company_facts` | Snapshot | MarketDataServiceExtensions |
| `list_available_agents` | Snapshot | OrchestrationServiceExtensions |
