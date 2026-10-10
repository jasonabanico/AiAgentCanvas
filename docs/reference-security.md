# Reference: Security and Governance

## Governance Kernel

The `GovernanceKernel` (from the `Microsoft.AgentGovernance` NuGet package) is the central governance object. It is constructed from `GovernanceOptions` and exposes three components:

- **PolicyEngine** -- evaluates tool calls against the governance policy rules.
- **AuditEmitter** -- emits structured audit events for policy checks, violations, and blocked calls.
- **InjectionDetector** -- scans system instructions for prompt injection patterns.

### Registration

Call `AddAiAgentCanvasSecurity()` in `Program.cs` before any other AI Agent Canvas service registration:

```csharp
builder.Services.AddAiAgentCanvasSecurity(builder.Configuration);
```

This registers the following components as singletons:

| Component | Purpose |
|-----------|---------|
| `GovernanceKernel` | Central governance object |
| `PolicyEngine` | Policy evaluation (extracted from kernel) |
| `AuditEmitter` | Audit event emission (extracted from kernel) |
| `GovernanceContextProvider` | Scans system instructions for prompt injection |
| `GovernedMcpGateway` | Evaluates tool calls against MCP gateway rules |
| `GovernanceToolWrapper` | Wraps all `AIFunction` instances with governance checks |
| Rate limiter | Fixed-window rate limiter (ASP.NET built-in) |

On the middleware side, `UseAiAgentCanvasSecurity()` enables the rate limiter, applies security headers, and subscribes to governance audit events for logging.

---

## Tool-Call Governance Pipeline

Every tool call passes through a five-step governance pipeline:

```
1. Tool call received
       |
2. Build McpGatewayRequest (agentId, toolName, payload)
       |
3. Evaluate against policy rules via GovernedMcpGateway
       |
4. Decision: Allow / Block / Audit
       |
5. Execute the tool (if allowed) or return a JSON error (if blocked)
```

### GovernedAIFunction

`GovernedAIFunction` extends `DelegatingAIFunction` (from `Microsoft.Extensions.AI`) and intercepts every tool call. It:

1. Serializes the call arguments to JSON.
2. Calls `GovernedMcpGateway.Evaluate()` to get an allow/block decision.
3. Emits an audit event via `AuditEmitter` (either `PolicyCheck` or `ToolCallBlocked`).
4. If blocked, returns a JSON error string: `{"error": "Tool 'X' was blocked by governance policy.", "status": "..."}`.
5. If allowed, delegates to the inner function via `base.InvokeCoreAsync()`.

### GovernanceToolWrapper

`GovernanceToolWrapper` implements `IToolGovernanceWrapper`. `AgentPipeline.WrapTools` calls `Wrap()` on each `AIFunction` and wraps the result in `TracedAIFunction`. The default agent, persona agents, the tools served by the MCP server, and connector tools are wrapped this way.

Three kinds of tool are not wrapped by the governance policy:

- Tools that `connect_mcp_server` registers at runtime go into the dynamic tool registry as they are. The default list blocks `connect_mcp_server` for this reason.
- Tools that skills register at runtime follow the same path.

### Approval-Required Tools

`Security:ApprovalRequiredTools` lists tool names that the governance gateway treats as needing approval. The list is a hard block: there is no approval prompt for these tools, and a call is refused until an operator removes the name from the list. The default is:

```json
"Security": {
  "ApprovalRequiredTools": [
    "system_write_file",
    "system_run_script",
    "connect_mcp_server",
    "schedule_task"
  ]
}
```

Each entry must match the name a tool registers under. An entry that matches no tool protects nothing, so a typo raises no error.

Connector tools use a different mechanism. A connector tool with risk `Send` or `Destructive` is wrapped in `ApprovalRequiredAIFunction`, which asks a person before the call runs. The default interactive agent has an approval channel and shows the prompt in the chat. A persona agent or an unattended run has no channel, so it does not execute the call. Set `Connectors:ApprovalMode` to `Audit` to run those tools without asking. Calls are still traced and counted.

---

## Policy Format

Governance policies are defined in YAML. The policy file path is set via `Security:PolicyPath` in configuration (default: `governance-policy.yaml`).

### Structure

```yaml
name: AiAgentCanvas-default
description: Default governance policy for AiAgentCanvas

rules:
  - name: block-dangerous-tools
    scope: tool_call
    action: deny
    condition:
      tool_name:
        in: [system_run_script]
    reason: "Shell execution requires explicit approval"

  - name: restrict-file-write-paths
    scope: tool_call
    action: deny
    condition:
      tool_name:
        equals: system_write_file
      path:
        matches: "^(/etc|/var|C:\\\\Windows|C:\\\\Program Files)"
    reason: "Writing to system directories is blocked"

  - name: block-private-mcp-endpoints
    scope: tool_call
    action: deny
    condition:
      tool_name:
        equals: connect_mcp_server
      endpoint:
        matches: "(localhost|127\\.0\\.0\\.1|169\\.254\\.169\\.254|10\\.|172\\.(1[6-9]|2[0-9]|3[01])\\.|192\\.168\\.)"
    reason: "MCP connections to private/internal addresses are blocked (SSRF protection)"

  - name: allow-all-other
    scope: tool_call
    action: allow
    condition: {}
```

### Rule Fields

| Field | Description |
|-------|-------------|
| `name` | Unique identifier for the rule |
| `scope` | What the rule applies to (currently `tool_call`) |
| `action` | `allow`, `deny`, or `audit` |
| `condition` | Match criteria using operators (see below) |
| `reason` | Human-readable explanation shown when the rule fires |

### Condition Operators

| Operator | Description | Example |
|----------|-------------|---------|
| `equals` | Exact string match | `tool_name: { equals: system_write_file }` |
| `in` | Matches any value in a list | `tool_name: { in: [system_run_script, exec] }` |
| `matches` | Regular expression match | `path: { matches: "^/etc" }` |

### Conflict Strategy

When multiple rules match a tool call, `ConflictResolutionStrategy.DenyOverrides` applies: any matching `deny` rule wins regardless of `allow` rules. This is the default and recommended setting.

---

## Rate Limiting

The platform registers ASP.NET's fixed-window rate limiter with a policy named `"agent"`. No endpoint applies the policy yet (see Known Gaps in [Platform Internals](reference-internals.md)), so it does not limit any request today.

| Parameter | Value |
|-----------|-------|
| Window | 1 minute |
| Permit limit | Configurable via `Security:RateLimitPerMinute` (default: 30) |
| Queue limit | 0 (no queuing; excess requests are rejected immediately) |

When the policy rejects a request, the server returns HTTP 429 with a JSON body:

```json
{
  "error": "Rate limit exceeded. Try again later."
}
```

---

## Security Headers

Three security headers are applied to HTTP responses through inline middleware in `UseAiAgentCanvasSecurity()`:

| Header | Value | Purpose |
|--------|-------|---------|
| `X-Content-Type-Options` | `nosniff` | Prevents MIME-type sniffing |
| `X-Frame-Options` | `DENY` | Prevents the page from being embedded in frames |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Limits referrer information sent to other origins |

---

## Authentication

Authentication is off by default. The Host logs a prominent warning at each start while it is off, because the endpoints it protects run shell commands, schedule unattended work and spend money. `AiAgentCanvas.Authentication` picks schemes by name from `Authentication:Schemes`. Two ship: `ApiKey` for machine callers and `JwtBearer` for any OIDC authority, which covers Entra ID, Auth0, Okta and Keycloak.

```json
{
  "Authentication": {
    "Enabled": true,
    "Schemes": ["ApiKey", "JwtBearer"],
    "AllowAnonymous": ["health"],
    "AllowedOrigins": ["https://app.example.com"],
    "ApiKey": { "HeaderName": "X-API-Key", "Keys": ["a-long-random-key"] },
    "JwtBearer": { "Authority": "https://login.example.com", "Audience": "agents" }
  }
}
```

Endpoints are protected with `RequireAgentAuthorization(auth, key)`, and `AllowAnonymous` lists the keys that stay open. The keys in use are `agui`, `a2a`, `devui`, `notifications`, `webhooks`, `health`, `runs`, `connections`, `connectors`, `structured`, `orchestrations` and `mcp`. API keys are compared in fixed time. Setting `AllowedOrigins` switches CORS from any origin to the named origins with credentials.

Two routes carry no endpoint authorization on purpose, because the caller is another service and sends none of our credentials:

- The **OAuth callback** (`/api/connections/oauth/callback`). The provider redirects the browser here. An encrypted, time-limited `state` value proves this host started the flow.
- The **connector webhook** (`/api/connectors/{connectionId}/webhook`). The connector checks the sender's own signature, and a request that fails is refused with 401 before anything is parsed.

---

## Credentials and Connections

Connection secrets are encrypted with ASP.NET Core Data Protection before they reach the database. Metadata stays readable and secrets do not. The key ring lives in `Connections:KeyRingPath`, and on Windows it is also wrapped with DPAPI. Whoever can read both the key ring and `connections.db` can read all stored secrets, so keep the key ring out of source control and out of the web root, and back up both together.

Secrets reach a connector only through its connection context. They do not appear in tool arguments, tool results, log lines or API responses. A connector's HTTP client allows only the hosts the connector names, over https (http for loopback only), follows no redirects, and retries only requests that are safe to repeat. The OAuth redirect address is built from `Connections:PublicBaseUrl` and not from the request, so a forged `Host` header cannot send an authorization code elsewhere.

---

## Other Exposure Controls

| Feature | Control |
|---|---|
| System tools | `SystemTools:AllowedPaths` and `AllowedCommands` both deny everything when empty. The Host defaults the path list to the `agent-workspace` folder and the command list to `dotnet`, `git`, `npm` and `node`. Commands run without a shell. |
| Vision | `Agent:Vision:AllowedPaths` and `AllowedUrlHosts` deny everything when empty. The fetch refuses private addresses at connect time. |
| MCP server | Exposes nothing until `Agent:McpServer:ExposedTools` names it. Leaves out tools that need approval. The Host refuses to start with it on and authentication off unless `AllowUnauthenticated` is set. |
| Orchestration | A person answers a plan review. No agent tool can. |
| Spend | `Agent:LoopGuard:MaxRunCost` caps a run, and `Agent:Budgets` caps daily spend per agent, per trigger and in total. |

---

## Production Checklist

Before deploying to production, verify each item:

1. **Set real API keys.** Replace placeholder values in `AIFoundry:Endpoint` and `AIFoundry:Key` with production credentials, or enable `UseAzureCredential` for managed identity. Keep secrets in environment variables or a secret store.
2. **Turn authentication on.** Set `Authentication:Enabled` to true, choose schemes, and list `AllowedOrigins`. Without it, endpoints are open to anyone who can reach the process.
3. **Configure governance policies.** Review `governance-policy.yaml` and add deny rules for tools that should not run in production.
4. **Review the approval-required list.** `schedule_task`, `system_write_file`, `system_run_script` and `connect_mcp_server` are blocked by default. Remove a name only when you accept what the tool can do.
5. **Review the tool allowlists.** Audit `SystemTools:AllowedPaths`, `SystemTools:AllowedCommands`, and the vision folders and hosts. Remove anything the deployment does not need.
6. **Set spend limits.** Fill in `Agent:Pricing` for the models in use, then set `Agent:LoopGuard:MaxRunCost` and `Agent:Budgets`. Budgets need the `RunLedger` flag.
7. **Protect the credential store.** Put `Connections:KeyRingPath` somewhere private, back it up with `connections.db`, and set `Connections:PublicBaseUrl` to the public https address.
8. **Configure HTTPS.** Run behind HTTPS termination, either a reverse proxy or Kestrel HTTPS.
9. **Set the logging level.** Change `Logging:LogLevel:Default` to `Warning` or `Error` to reduce volume and avoid logging sensitive data.
10. **Remove DevUI in production.** Wrap the `AddDevUI()` and `MapDevUI()` calls in an environment check so the development UI is not exposed.
11. **Review MCP connection security.** The default policy blocks `connect_mcp_server` calls to private and internal addresses. Verify the rule is active. Prefer a connector for any server that needs credentials.
