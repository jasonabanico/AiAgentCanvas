# Operations and Connectors

This guide covers two groups of features. The first group serves agents that run without a person watching: a record of each run, spend limits, durable event triggers and deterministic jobs. The second group serves agents that act inside outside accounts: stored credentials, OAuth, and connectors to services such as Twilio and Gmail.

All of it is off by default and sits behind `Features:*` flags.

| Flag | Adds | Needs |
|------|------|-------|
| `RunLedger` | A record of each run, the `/api/runs` endpoints, and the Runs tab | none |
| `Jobs` | Deterministic jobs that need no model | none |
| `Connections` | Encrypted credentials, the OAuth connect flow, token refresh | none |
| `Connectors` | Connectors, their tools, webhooks and events | `Connections` |

`Agent:Budgets` is a configuration section and not a flag. It needs `RunLedger` and `Agent:Pricing`, and the Host refuses to start with budgets enabled and no ledger.

## Run ledger

An unattended agent leaves no trace in a chat window. The run ledger writes one record per run to SQLite: what started it, what it did, what it cost and how it ended.

A record holds the agent name, the source (`Interactive`, `Scheduled`, `Trigger`, `Handoff`, `Job`, `Orchestration` or `External`), the trigger or task id, start and end times, status, the input and output text, token counts, estimated cost, the tool calls with outcome and duration, and the reason a run was cut short, if it was.

- **Nesting.** A run started inside another run, such as a handoff, records its parent. The parent's totals include the child's usage. Totals reported by the API count top-level runs only, so delegated work is not counted twice.
- **Restarts.** A run still marked `Running` when the process starts again is marked `Abandoned`. Its outcome is unknown.
- **Text limits.** Input and output are cut to `MaxTextChars`.
- **Scope.** Scheduled tasks, triggers, handoffs and jobs are recorded. Chat sessions through the AG-UI endpoint are not.

| Setting (`Agent:RunLedger`) | Default | Meaning |
|---|---|---|
| `DatabasePath` | `run-ledger.db` | SQLite file |
| `RetentionDays` | 30 | Finished runs older than this are deleted daily |
| `MaxTextChars` | 4000 | Longest stored input or output |

Read the ledger through the **Runs** tab, the endpoints (`GET /api/runs`, `/api/runs/totals`, `/api/runs/{id}`, endpoint key `runs`), or the agent tools `list_recent_runs`, `get_run` and `run_totals`. The list takes `hours`, `agent`, `status`, `source` and `triggerId` filters.

## Spend limits

Each model call is priced from `Agent:Pricing`, which lists the rate per million tokens for each model. Two limits use those prices.

**Per run.** `Agent:LoopGuard:MaxRunCost` ends a run once it has spent that amount. The loop guard withdraws the tools and asks the model to report what it has. The ledger records the termination as `CostBudget`.

**Per day.** `Agent:Budgets` limits unattended spend over a rolling 24 hours, measured from the ledger, so it survives a restart.

```json
{
  "Agent": {
    "Pricing": { "Models": { "gpt-4o": { "InputPer1M": 2.5, "OutputPer1M": 10 } } },
    "LoopGuard": { "MaxRunCost": 0.50 },
    "Budgets": {
      "Enabled": true,
      "TotalDailyLimit": 20,
      "DailyLimit": 5,
      "Agents": { "FinancialAnalyst": 10 },
      "Triggers": { "reply-to-texts": 3 }
    }
  }
}
```

A limit of zero means no limit. Before a scheduled task or trigger event starts a model run, the budget guard checks the limits for the agent, the trigger and the total.

- A **trigger event** over its limit is deferred without spending an attempt, and the guard is asked again after `DeferMinutes`.
- A **scheduled agent task** over its limit is skipped for that tick. Job tasks, which use no model, still run.
- A notification goes out once per day when a limit passes `WarnAtFraction` (default 0.8) and once when it is reached.

A run that is still in flight does not count until it ends, so concurrent runs can pass a limit by their own cost. Set `MaxRunCost` to bound that overshoot.

## Durable event triggers

Event triggers hold their queue in SQLite. A restart loses no pending event and a full queue refuses events and says so, where an in-memory channel dropped them without notice.

Delivery is at least once. An event interrupted mid-run runs again, so the work an agent does for an event must be safe to repeat. Send tools accept an idempotency key for this reason.

- **Deduplication.** An event carries an optional dedupe key. A second event with the same trigger and key is dropped. Scheduled triggers key by minute, file watchers key by path, change type and second, and a webhook sender sets the `Idempotency-Key` header.
- **Retry.** A failed run is retried with exponential backoff up to `MaxAttempts`. After that the event is parked as `Dead` and a notification goes out.
- **Inspection.** `GET /api/triggers/queue` shows counts. `GET /api/triggers/events/dead` lists parked events with their last error. `POST /api/triggers/events/{id}/retry` gives one a fresh set of attempts. The tools `trigger_queue_status`, `list_failed_trigger_events` and `retry_trigger_event` do the same for an agent.
- **Webhooks.** A body over `MaxWebhookBodyBytes` gets 413. A full queue gets 503 with `Retry-After`.

| Setting (`Agent:EventTriggers`) | Default |
|---|---|
| `DatabasePath` | `event-triggers.db` |
| `QueueCapacity` | 256 |
| `MaxAttempts` | 3 |
| `RetryBaseSeconds` | 30 |
| `MaxRetryDelayMinutes` | 15 |
| `SucceededRetentionDays` | 7 |
| `MaxWebhookBodyBytes` | 262144 |
| `RunTimeoutMinutes` | 10 |

## Jobs

Some scheduled work needs no model: prune a table, post a nightly summary, check a queue. A job is a class that implements `IAgentJob`.

```csharp
public sealed class NightlyReportJob : IAgentJob
{
    public string Name => "nightly-report";
    public string Description => "Posts yesterday's run totals.";

    public async Task<JobResult> RunAsync(JobContext context, CancellationToken ct)
    {
        // read arguments from context.Arguments, do the work, return a result
        return JobResult.Success("done");
    }
}

services.AddAgentJob<NightlyReportJob>();
```

Job names are lowercase letters, digits and hyphens. A job reports a problem it found by returning `JobResult.Failure`. An exception means the job itself broke. A job does not overlap itself: a second start while one is running is skipped. A job that runs longer than `Agent:Jobs:TimeoutSeconds` (default 600) is cancelled and recorded as failed. Each run appears in the ledger with source `Job`.

A job can be started from a scheduled task (`schedule_job`), from a trigger (`targetJob` on `create_trigger` or `create_connector_trigger`), or by an agent with `run_job`. `list_jobs` lists what is registered. Two jobs ship with the platform: `run-failure-report` summarizes recent failed runs and notifies, and `trigger-queue-health` reports parked events.

## Connections

A connection is one configured account for one connector: a Gmail mailbox or a Twilio account. Enable `Connections` to store them.

**Where secrets live.** Metadata (label, status, settings) is stored in the clear in `connections.db`. Secrets are encrypted with ASP.NET Core Data Protection before they reach the database. The key ring lives in `KeyRingPath` and, on Windows, is also wrapped with DPAPI. Whoever can read both the key ring and the database can read all stored secrets, so keep the key ring out of source control and out of the web root. Back up both files together: without the key ring the stored secrets cannot be read.

No endpoint returns a secret. The connection list shows status, scopes and settings only.

**Key connections.** `POST /api/connections/key` stores an API key or a key pair (for Twilio, an API key SID and secret). Extra secrets, such as a webhook signing token, go in `extras`. The **Connections** tab has a form for this.

**OAuth connections.** The OAuth flow uses the authorization code grant with PKCE. The `state` value is encrypted and expires, so the callback completes only a flow this host started. Built-in providers are `google`, `microsoft`, `slack`, `github`, `notion` and `hubspot`. Add or override one under `Connections:OAuthProviders`.

1. Register an OAuth application with the provider. Set its redirect address to `{PublicBaseUrl}/api/connections/oauth/callback`.
2. Enter the client id and secret on the Connections tab, or set `Connections:OAuthApps:<provider>` in configuration.
3. Choose the connector and select **Connect**.

`Connections:PublicBaseUrl` is required for OAuth. The redirect address is built from it and not from the request, so a forged `Host` header cannot send an authorization code elsewhere.

**Refresh.** An access token within `RefreshSkewSeconds` of expiry is refreshed before it is handed out. Refreshes are serialized per connection, because providers that rotate refresh tokens invalidate the old one at once and a second concurrent refresh would fail. A background sweep refreshes tokens due to expire soon. When a provider answers `invalid_grant`, the connection becomes `NeedsReauth`, its tools stop, and one notification asks a person to reconnect.

| Setting (`Connections`) | Default |
|---|---|
| `DatabasePath` | `connections.db` |
| `KeyRingPath` | `data-protection-keys` |
| `ProtectKeysWithDpapi` | true (Windows only) |
| `PublicBaseUrl` | none |
| `ReturnUrl` | `/` |
| `RefreshSkewSeconds` | 120 |
| `OAuthApps`, `OAuthProviders` | empty |

The management endpoints use endpoint key `connections`. The OAuth callback has no authorization attribute, because the browser arrives from the provider carrying no credentials of ours.

## Connectors

A connector turns a service into tools, events or documents. The full contract is in `docs/design/connectors.md`. This section describes what an operator sees.

Enable `Connectors` (and `Connections`). The Host registers the shipped connectors, runs one instance per stored connection, and reconciles every `ReconcileSeconds` (default 15), so a connection added on the Connections tab starts within seconds.

**Tool names.** Tools are named `{prefix}_{verb}`. The first connection of a connector uses the connector's own prefix (`twilio_sms_send`). A later connection adds its label (`twilio_sms_frontdesk_send`) so two accounts do not share names. If the first connection is removed, the next one takes the plain prefix.

**Risk and approval.** Each tool declares a risk: `Read`, `Write`, `Send` or `Destructive`. A tool that sends to another person or deletes is wrapped in an approval requirement. The default interactive agent shows the approval prompt in chat. Agents without an approval channel, and unattended runs, do not execute the call. Where agents must send without a person present, set `Connectors:ApprovalMode` to `Audit`. Calls are still traced and counted.

**Network rules.** A connector cannot build its own HTTP client. The one it receives:

- sends only to hosts in the connector's allowed list, over https (http is accepted for loopback only);
- follows no redirects;
- attaches the stored credential and, on a 401 with an OAuth token, refreshes once and retries once;
- retries only requests that are safe to repeat, so the host does not repeat a send automatically;
- reports `aiagentcanvas.connector.calls` and `aiagentcanvas.connector.duration`, and logs the host and path but not the query string or any header.

**Health.** The host checks each connection every `CheckIntervalSeconds` (default 300). A connection the service refuses becomes `NeedsReauth`. A connection that fails for another reason shows `Error` with the detail, and clears when a later check passes.

**Webhooks and events.** A connector with the Events capability receives webhooks at `/api/connectors/{connectionId}/webhook`. The route carries no endpoint authorization. The connector checks the sender's own signature and the route refuses a failed check with 401 before parsing anything. The Connections tab shows each connection's webhook address.

A verified event goes to the trigger system. Create a trigger for it:

```
create_connector_trigger(name: "reply to texts", connectionId: "tw1",
    eventType: "sms.received", agentMessage: "Draft a reply to this text.")
```

`eventType` matches exactly, or by family with a trailing `.*` (`sms.*`). The event id is the dedupe key, so a service that delivers the same webhook twice runs the agent once. The sender's text reaches the agent after a line that marks it as data from an outside party. A full queue answers the webhook with 503 so the service retries.

### Twilio SMS

| Setting | Meaning |
|---|---|
| `account_sid` | Required. The account SID (`AC` and 32 hex digits) |
| `from_number` | A sender number in E.164 form. Either this or `messaging_service_sid` is required |
| `messaging_service_sid` | A Messaging Service SID to send from |
| `quiet_hours` | For example `21:00-08:00`. Sends in this window are refused |
| `timezone` | IANA zone for quiet hours. Default UTC |
| `max_sends_per_hour` | Default 30 |

Credentials are a key pair: an API key SID and secret, or the account SID and auth token. Webhooks need the account auth token stored as the `AuthToken` extra, which verifies `X-Twilio-Signature`.

Tools: `twilio_sms_send` (risk Send), `twilio_sms_list`, `twilio_sms_get` and `twilio_sms_missed_calls` (risk Read). Events: `sms.received`, `sms.failed` and `call.missed`.

Protections in the send tool: the number must be E.164, the body is capped at 1600 characters, a recipient who replied STOP is refused until they reply START, and the quiet-hours and hourly limits apply. A repeated `idempotencyKey` within 24 hours returns the first result and sends nothing. Opt-outs, the idempotency window and the hourly count are held in memory, so they reset on restart. Twilio enforces opt-outs itself, and the connector learns of one from the first refused send.

### Gmail and other MCP servers

The MCP connector runs an MCP client over the connection's guarded HTTP client, so the credential comes from the store and the token refreshes. Nothing about the account passes through the model. This replaces typing a bearer token into `connect_mcp_server`.

| Setting | Meaning |
|---|---|
| `endpoint` | Required. The server's https address. Calls may reach only this host |
| `include` | Optional comma-separated tool names to expose. Empty exposes all |
| `risk.<tool>` | Optional override: `Read`, `Write`, `Send` or `Destructive` |

Gmail is the generic connector with Google OAuth and the scopes `gmail.readonly` and `gmail.compose`. It reads mail and creates drafts. Add `gmail.send` when connecting only if agents should send. The `endpoint` must be an MCP server that fronts Gmail. No address is built in.

An MCP server describes its own tools, so the connector decides risk itself, in this order: your `risk.<tool>` setting, then the definition's fixed risks, then the tool name. A name with a delete-like word (`delete`, `remove`, `trash`, `purge`, `cancel` and others) is `Destructive`. A name with a send-like word (`send`, `post`, `publish`, `reply`, `forward`) is `Send`. A server cannot talk a tool down from these. Only then are the server's `destructiveHint` and `readOnlyHint` used, and a name with a read-like word (`get`, `list`, `search`) is `Read`. Everything else is `Write`. A misspelled `risk.` value stops the connection from starting.

If the server drops its session, the next check reconnects and the tools are fetched again.

### Writing a connector

1. Implement `IConnectorDefinition`: a `ConnectorDescriptor` (id, auth kind, allowed hosts, capabilities) and a factory.
2. Implement `IConnector` plus `IToolConnector`, `IEventSourceConnector` or both. Build tools with `ConnectorTools.Create`, which sets the prefixed name and the risk.
3. Get credentials from `IConnectionContext.GetCredentialAsync` and the HTTP client from `CreateHttpClient`. Return failures as `ConnectorTools.Error(code, message)`.
4. Register with `services.AddConnector(definition)`.
5. Add a test class that derives from `ConnectorConformance` in `tests/AiAgentCanvas.Tests`. It checks tool naming, descriptions, declared risk, capability flags, check behavior against a failing service, double disposal and webhook rejection.

## Limits to know

- **Approval list.** `Security:ApprovalRequiredTools` blocks a tool outright. It does not ask. `schedule_task`, `system_write_file`, `system_run_script` and `connect_mcp_server` are blocked by default and have no approval path. Connector tools use the approval requirement described above and are not in that list.
- **Runtime tools.** Tools that arrive while the host runs, from connectors and from servers connected with `connect_mcp_server`, are wrapped for governance and tracing and reach agents on their next call. An agent with a tool seed sees only the runtime tools its seed names.
- **Chat runs.** The ledger and the daily budgets cover unattended work. A chat session is not recorded and not limited by `Agent:Budgets`.
- **Approval channel.** Only the default interactive agent has an approval channel. A persona agent that calls a send tool does not execute it.
