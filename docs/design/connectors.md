# Connector Standard

Status: implemented. The contracts, host, credential store, Twilio SMS connector and MCP adapter are built and tested. Section 0 lists where the build differs from this text. Where a later section disagrees with section 0, section 0 is correct.

## 0. As built

Code: `src/Platform/AiAgentCanvas.Connectors` (contracts and host), `src/Platform/AiAgentCanvas.Connections` (credentials), `src/Connectors/AiAgentCanvas.Connector.TwilioSms`, `src/Connectors/AiAgentCanvas.Connector.Mcp`. Tests: `tests/AiAgentCanvas.Tests` (`ConnectorFrameworkTests`, `TwilioSmsTests`, `McpConnectorTests`, `ConnectorEndToEndTests`, `ConnectionsTests`). Operator documentation: `docs/guide-11-operations-and-connectors.md`.

| Topic | Decision as built |
|---|---|
| Registration | Connectors register through `services.AddConnector(definition)` in the Host behind `Features:Connectors`, and the Host references them directly. They are not `IServiceModule` plugins. A connector runs only when a connection for it exists, so no per-connector `Enabled` flag is needed. |
| Several connections | The first connection of a connector uses the plain tool prefix (`twilio_sms_send`). Later ones add a slug of their label (`twilio_sms_frontdesk_send`). `IConnectionContext.ToolPrefix` carries it. Open question 1 is closed. |
| Approval | A `Send` or `Destructive` tool is wrapped in `ApprovalRequiredAIFunction`, outermost, after governance and tracing. It is not added to `Security:ApprovalRequiredTools`, because that list blocks a tool and does not ask. `Connectors:ApprovalMode` is `Require` or `Audit` for the whole host. |
| Conformance suite | `ConnectorConformance` is an abstract xunit class in the test assembly, so a failing connector fails the build. It checks tool naming, descriptions, declared risk, capability flags, the check against a failing service and against a 401, double disposal and webhook rejection without a signature. Section 12 lists further checks that are not automated: secrets absent from logs and spans, and per-tool argument names. The MCP connector is covered by its own tests, not by the shared suite. |
| HTTP pipeline | Outermost to innermost: the connector handler (host allowlist, https only except loopback, credential attach, one refresh and retry on 401, counter and histogram), then the named client's standard resilience handler with retries limited to safe methods, then a primary handler with redirects off. There is no circuit breaker per connection and no per-call `Activity`. |
| Webhooks | `/api/connectors/{connectionId}/webhook` has no endpoint authorization. The connector's signature check is the authentication. The address a signature covers is built from `Connections:PublicBaseUrl`. |
| Events | The sink is `IConnectorEventSink`, implemented by `ConnectorEventBridge` in the EventTriggers capability. Events go to the durable trigger queue and not to a `Channel`. A connector trigger (`create_connector_trigger`) names a connection and an event type or family. The event id is the dedupe key. |
| Credentials | `ICredentialProvider` has `GetAsync`, `RefreshAsync` and `MarkNeedsReauthAsync`. The store encrypts with Data Protection. The development-only provider in the work plan was not built. |
| Twilio | Settings use snake case (`account_sid`, `from_number`, `quiet_hours`, `timezone`, `max_sends_per_hour`). The missed-call tool is `twilio_sms_missed_calls`. Opt-outs, the idempotency window and the hourly count are held in memory. There is no `AllowedRecipients` setting and no per-recipient consent record. |
| MCP and Gmail | The adapter runs `McpClient` over the connection's HTTP client, so credentials and refresh come from the store, and the MCP SDK's own OAuth types are not used. Risk is decided in this order: a `risk.<tool>` setting, the definition's fixed risks, a destructive or send word in the tool name, the server's hints, a read word in the name, then `Write`. A server cannot lower a tool below what its name implies. The expected-issuer check from `McpConnectionManager` is not carried over. No Gmail endpoint is built in. |
| Migration | `McpConnectionManager` and `connect_mcp_server` are unchanged and still take a token as an argument. Removing them, and persisting their connections, is not done. |
| Frontend | The Connections tab lists connections, adds key and OAuth connections, checks, reconnects and removes. |


## 1. Purpose

AiAgentCanvas agents need to reach many external services: email, SMS, CRM, accounting, calendars and more. Each service differs in transport (REST, vendor SDK, OpenAPI client, MCP server), in authentication (API key, key pair, OAuth 2.0) and in what it offers (actions, inbound events, documents). This document defines one standard structure so that:

- an agent sees only tools, whatever the transport behind them;
- authentication, retries, logging and governance are applied once, in the framework, and a connector author does not handle a secret directly;
- each connector reports an honest status, so the platform can tell a working connection from a broken one;
- a new connector passes a shared conformance suite before it ships.

## 2. Scope

In scope: the connector contracts, the registry and catalog, tool conventions, governance and event integration, the conformance tests, and two reference connectors (Twilio SMS over REST, Gmail through an MCP client adapter).

Out of scope, specified separately:

- The credential and token store (OAuth flows, encryption at rest, refresh). This document assumes the interface in section 6.
- Category interfaces such as `ICrmProvider` and `IEmailProvider`. They are deferred until two real vendors exist for a category (section 14).
- A connections page in the frontend.

## 3. Terms

| Term | Meaning |
|---|---|
| Connector | Code that integrates one service, such as `twilio-sms` or `gmail`. Registered once. |
| Connection | One configured instance of a connector with its own credentials and settings, such as two Gmail accounts. |
| Capability | What a connector offers: tools, inbound events, or documents. A connector implements the capability interfaces it needs. |
| Tool | An `AITool` the agent can call. The only thing an agent sees. |
| Transport | How the connector reaches the service: REST, SDK, OpenAPI client, or MCP client. Hidden from the agent. |

## 4. Where it lives

- New project `src/Platform/AiAgentCanvas.Connectors` holds the contracts, registry, catalog, conformance test base and framework services. It follows the Authentication project's precedent: Abstractions stays free of web dependencies.
- Each connector is its own project under a new solution folder `src/Connectors/`, named `AiAgentCanvas.Connector.<Name>`.
- Each connector project exposes an `IServiceModule` with section `Connectors:<id>`. The existing rule applies: a module loads only when `Enabled` is `true`.
- Existing `DataConnection.*` projects stay as they are. Migrating them is a later decision.

## 5. Contracts

The code below is a sketch. Names may change in review.

```csharp
namespace AiAgentCanvas.Connectors;

public enum AuthKind { None, ApiKey, KeyPair, OAuth2 }

[Flags]
public enum ConnectorCapabilities { None = 0, Tools = 1, Events = 2, Documents = 4 }

public sealed record ConnectorDescriptor(
    string Id,                              // stable, lowercase, hyphenated: "twilio-sms"
    string DisplayName,
    string Category,                        // "messaging", "email", "crm", ...
    AuthKind Auth,
    IReadOnlyList<string> Scopes,           // OAuth scopes or API permissions required
    ConnectorCapabilities Capabilities);

public enum ConnectorState { Connected, NotConfigured, NeedsReauth, Error }

public sealed record ConnectorStatus(ConnectorState State, string Detail, DateTimeOffset CheckedAt);

/// Registered once per connector type. The registry calls Create once per connection.
public interface IConnectorDefinition
{
    ConnectorDescriptor Descriptor { get; }
    IConnector Create(IConnectionContext context);
}

public interface IConnector : IAsyncDisposable
{
    Task StartAsync(CancellationToken ct);                 // open sessions, validate settings
    Task<ConnectorStatus> CheckAsync(CancellationToken ct);// must not throw; report Error instead
}

public interface IToolConnector : IConnector
{
    Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken ct);
    event EventHandler? ToolsChanged;                      // MCP servers can change their list
}

public interface IEventSourceConnector : IConnector
{
    Task<EventBatch> PollAsync(string? cursor, CancellationToken ct);       // pull mode
    bool VerifyWebhook(WebhookRequest request, out string? failureReason);  // push mode
    IReadOnlyList<ConnectorEvent> ParseWebhook(WebhookRequest request);
}

public interface IDocumentSourceConnector : IConnector
{
    IAsyncEnumerable<SourceDocument> ListChangesAsync(string? cursor, CancellationToken ct);
}

public interface IConnectionContext
{
    string ConnectionId { get; }
    string ConnectorId { get; }
    IReadOnlyDictionary<string, string> Settings { get; }  // non-secret: sender number, base URL
    ValueTask<ConnectorCredential> GetCredentialAsync(CancellationToken ct);
    HttpClient CreateHttpClient();                         // REST connectors only
}

/// ToString returns "[redacted]" so a credential cannot reach a log by accident.
public sealed record ConnectorCredential(AuthKind Kind, string Value, string? Secret = null)
{
    public override string ToString() => "[redacted]";
}

public enum ToolRisk { Read, Write, Send, Destructive }

public sealed record WebhookRequest(string Url, IReadOnlyDictionary<string, string> Headers, ReadOnlyMemory<byte> Body);
public sealed record ConnectorEvent(string Id, string Type, DateTimeOffset At, string Summary, IReadOnlyDictionary<string, string> Data);
public sealed record EventBatch(IReadOnlyList<ConnectorEvent> Events, string? NextCursor);
public sealed record SourceDocument(string Id, string Title, string Text, string? Url, DateTimeOffset ModifiedAt, bool Deleted);
```

Rules for the contracts:

1. A connector implements `IToolConnector` and, optionally, the other capability interfaces. MCP client adapters usually implement `IToolConnector` only.
2. `CheckAsync` must not throw. A failed call returns `ConnectorState.Error`; a 401 or a refused refresh returns `NeedsReauth`.
3. `GetToolsAsync` may be called again after `ToolsChanged`. The registry replaces the connector's tools in `DynamicToolRegistry` under the key `connector:<connectionId>`.
4. `CreateHttpClient` returns a client from `IHttpClientFactory` with the framework handlers already attached (section 7). A connector must not build its own `HttpClient`.

## 6. Credentials

The connector standard depends on the credential store specified separately. It needs only this interface:

```csharp
public interface ICredentialProvider
{
    ValueTask<ConnectorCredential> GetAsync(string connectionId, CancellationToken ct);
    Task MarkNeedsReauthAsync(string connectionId, string reason, CancellationToken ct);
}
```

Rules:

- Credentials reach a connector only through `IConnectionContext`. They must not appear in tool arguments, tool results, log messages, audit entries or exception messages.
- A tool that accepts a secret as an argument is rejected by the conformance suite. This replaces today's `connect_mcp_server` tool, which takes `bearerToken` and `apiKey` as model-visible arguments.
- The store is `AiAgentCanvas.Connections`. See section 0.

## 7. Framework pipeline for REST connectors

`IConnectionContext.CreateHttpClient()` composes these handlers, outermost first:

1. Resilience: `Microsoft.Extensions.Http.Resilience` with a timeout, retry with jitter, and a circuit breaker per connection.
2. Telemetry: an `Activity` per call and a duration histogram, tagged with connector id and outcome.
3. Authentication: attaches the credential. On a 401 it asks `ICredentialProvider` for a fresh credential once, then retries once. A second 401 marks the connection `NeedsReauth`.
4. Redaction: strips `Authorization` and similar headers and known secret fields from any logged request or response.
5. Host allowlist: the base address comes from the connector descriptor or connection settings. A request to any other host is refused. Tool arguments must not supply a URL.

## 8. Tools

Conventions for each tool a connector exposes:

- Name: `<connector>_<verb>_<noun>` in snake case, such as `twilio_sms_send`. Names must be unique across the registry.
- Description: one sentence for the model, stating what the tool does and what it returns.
- Risk: each tool carries a `ToolRisk`. Create tools with a helper that sets `AdditionalProperties["connector.id"]` and `AdditionalProperties["connector.risk"]` through `AIFunctionFactoryOptions`.
- Results: a JSON string. Failures return `{ "error": "<code>", "message": "<text>" }` and do not throw. Results are capped at a configured size, and lists are paginated with a cursor.
- Send tools accept an idempotency key where the service supports one.

## 9. Governance and approval

- Each tool is wrapped by the existing `IToolGovernanceWrapper` like any other.
- At registration, the host wraps each tool with risk `Send` or `Destructive` in `ApprovalRequiredAIFunction`. The wrapper asks a person before the call runs. It does not use `Security:ApprovalRequiredTools`, which blocks a tool outright.
- `Connectors:ApprovalMode` is `Require` by default and may be set to `Audit` for the host once the operator trusts its connectors. `Audit` still records each call. Only the default interactive agent has an approval channel, so a persona agent or an unattended run does not execute an approval-gated call.
- Per-connection rate limits and quiet hours are connector settings, enforced by the Send tools before they call the service.
- A persona reaches a connector's tools only if its `IAgentToolsSeed` names them. There is no wildcard.
- Policy rules in `governance-policy.yaml` may match on tool name or on `connector.risk`.

## 10. Events

- Pull mode: a scheduler calls `PollAsync` with the stored cursor and writes the new cursor after the events are queued.
- Push mode: each connector with webhooks gets a route `/api/connectors/{connectionId}/webhook`. It carries no endpoint authorization, because the sender holds none of our credentials, and the connector's signature check is the authentication. The route reads the raw body with a size limit, calls `VerifyWebhook`, and returns 401 on failure before parsing anything. The existing `/api/triggers/webhook/{triggerId}` route has no signature check and is not used for connectors.
- Verified events are deduplicated by `ConnectorEvent.Id`, mapped to the existing `TriggerEvent`, and written to the bounded `Channel<TriggerEvent>`. The existing `TriggerDispatchService` runs the agent.
- An event's `Summary` is untrusted input. It is wrapped as data in the agent message, in the same way as any other tool result.

## 11. Observability

- Spans: `connector <id> <tool>` with tags `connector.id`, `connection.id` and `tool.name`.
- Metrics added to `AgentTelemetry`: `aiagentcanvas.connector.calls`, `aiagentcanvas.connector.duration` and `aiagentcanvas.connector.errors`, tagged by connector and outcome.
- `CheckAsync` runs on an interval. Results feed an `IHealthCheck` per connection and the connections list. A change to `NeedsReauth` sends a notification through `INotificationSink`.
- Audit: connection created or removed, status change, and each call to a Send or Destructive tool.

## 12. Conformance tests

A base class `ConnectorConformance` in the test assembly (`tests/AiAgentCanvas.Tests/TwilioSmsTests.cs`) runs against each connector with a fake HTTP handler or a fake MCP server. It checks that:

1. the descriptor is valid and its id matches the configuration section;
2. `CheckAsync` returns `NotConfigured` with no credentials and does not throw;
3. a 401 from the service yields `NeedsReauth` after one refresh attempt;
4. tool names are unique, correctly prefixed and within the length limit;
5. each tool declares a risk, and no tool takes a parameter named like a secret;
6. a tool result for a failing call is the structured error and not an exception;
7. credentials do not appear in captured logs, spans or tool results;
8. a request to a host outside the allowlist is refused;
9. an invalid webhook signature is rejected before parsing;
10. disposal releases the connection.

Live tests against real services are opt-in through environment variables and are excluded from CI.

## 13. Reference connectors

Both are built before the interfaces are frozen. If either requires a change to the contracts, the contracts change first.

### 13.1 Twilio SMS (REST)

| Item | Decision |
|---|---|
| Id | `twilio-sms`, category `messaging` |
| Auth | `KeyPair`: account SID plus API key SID and secret |
| Capabilities | Tools and Events (push) |
| Transport | `HttpClient` from the framework pipeline against `api.twilio.com`. The official SDK is not used, so that auth, resilience and redaction stay in one pipeline. Revisit if the SDK proves necessary. |
| Settings | `AccountSid`, `FromNumber` (or messaging service), `QuietHours`, `AllowedRecipients` for development |

Tools:

| Tool | Risk | Purpose |
|---|---|---|
| `twilio_sms_send` | Send | Send a message to one number. Refuses a recipient on the opt-out list. |
| `twilio_sms_list` | Read | List recent messages with a cursor. |
| `twilio_sms_get` | Read | Fetch one message and its delivery status. |
| `twilio_call_list_missed` | Read | List recent calls with status `no-answer` or `busy`. |

Events: inbound SMS and missed-call status callbacks. Twilio signs webhooks with `X-Twilio-Signature`, computed with the account auth token. Confirm whether an API key secret can validate signatures; if not, the connection must also hold the auth token, and the credential store must support it.

Compliance requirements for the connector:

- record consent per recipient and honor opt-out keywords before any send;
- enforce quiet hours and a per-connection send rate;
- follow the sender registration rules of the country in use, such as Australian sender ID rules or US A2P registration. Check the current rules before go-live.

Tests specific to this connector: send refused for an opted-out number; send refused outside quiet hours; signature check with a known-good and a tampered request; missed-call event produces one `TriggerEvent` when delivered twice.

### 13.2 Gmail (MCP client adapter)

| Item | Decision |
|---|---|
| Id | `gmail`, category `email` |
| Auth | `OAuth2` with Google as the provider, through the credential store. The connection's HTTP client attaches and refreshes the token. |
| Capabilities | Tools only |
| Transport | A generic `McpConnector` using `McpClient` over HTTP to the endpoint in configuration. The registry listing shows `https://gmailmcp.googleapis.com/mcp/v1`; verify against Google's documentation. |
| Settings | `Endpoint`, `ExpectedIssuer`, `Tools.Include`, `Tools.Risk` overrides |

Adapter behavior:

- Tools come from `ListToolsAsync` at start. `ToolsChanged` fires when the server sends a list-changed notification.
- Tool names are prefixed with `gmail_`.
- `Tools.Include` limits what agents see. The registry listing showed about 30 tools, and each definition costs context budget.
- Risk comes from configuration, not from the server. Example: `create_draft` is Write, `get_message` is Read, `forward` is Send, `delete_label` is Destructive. A tool with no configured risk defaults to Write. MCP annotations such as read-only and destructive are accepted as hints only and may lower a tool to Read only when the operator opts in.
- The expected-issuer check from `McpConnectionManager` is kept.
- On reconnect, the adapter obtains a fresh credential and does not reuse a stored string.

Tests specific to this connector: tool-list change updates the registry; a tool outside `Tools.Include` is not exposed; unknown tools default to Write; a revoked token yields `NeedsReauth`.

## 14. Deferred: category interfaces

Interfaces such as `IEmailProvider`, `ICalendarProvider`, `ICrmProvider` and `IMessagingChannel` let an agent work with interchangeable vendors. They limit a connector to the features common to all vendors, so they are written only when two vendors in a category are in use. Likely first candidates: messaging (Twilio plus WhatsApp), email (Gmail plus Outlook), calendar, CRM.

## 15. Migration

- `McpConnectionManager` becomes the base of `McpConnector`. Its three tools (`connect_mcp_server`, `disconnect_mcp_server`, `list_mcp_connections`) are replaced by connection management in the connections API. `connect_mcp_server` stays in the default approval list until it is removed.
- `IMcpConnectionSeed.BearerToken` and `ApiKey` are deprecated in favor of a connection reference.
- Connection configuration persists across restarts. Today it lives in memory only.

## 16. Acceptance criteria

The interfaces are frozen when:

1. Both reference connectors pass the conformance suite with no change to the contracts after the second connector.
2. A persona, for example the missed-call responder, runs end to end using only tool names from its `IAgentToolsSeed`.
3. A Send tool pauses for approval by default and completes after approval.
4. The documentation (section 4 of the architecture guide, a connector authoring guide) is updated.

## 17. Work plan

| Phase | Content | Estimate |
|---|---|---|
| 0 | Development-only credential provider (user secrets) | 1-2 days |
| 1 | Contracts, registry, catalog, framework pipeline, conformance suite | 5-7 days |
| 2 | Twilio SMS connector with webhook route | 4-5 days |
| 3 | MCP client adapter and Gmail configuration | 3-4 days |
| 4 | Fix contract gaps found by 2 and 3, freeze, documentation | 2-3 days |

These are estimates for one engineer and exclude the OAuth credential store.

## 18. Open questions

1. Closed. Tool names carry the connection label for every connection after the first. See section 0.
2. Whether to move to MCP SDK 2.x, which was released after the pinned 1.4.0. It may change the OAuth types the adapter uses.
3. Scope of a connection: one per deployment, per user, or per agent. The credential store spec decides this; the registry must allow all three.
4. Whether `DataConnection.*` projects migrate to this standard or stay separate.
5. Tool selection when the registry holds more tools than the context budget allows. A per-run tool filter is likely needed.
