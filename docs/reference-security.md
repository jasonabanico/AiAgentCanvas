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

`GovernanceToolWrapper` implements `IToolGovernanceWrapper`. At startup, the orchestration layer calls `Wrap()` on every registered `AIFunction`, replacing the original with a `GovernedAIFunction`. This ensures governance applies to all tools without requiring each tool to implement its own checks.

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
        in: [run_script]
    reason: "Shell execution requires explicit approval"

  - name: restrict-file-write-paths
    scope: tool_call
    action: deny
    condition:
      tool_name:
        equals: write_file
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
| `equals` | Exact string match | `tool_name: { equals: write_file }` |
| `in` | Matches any value in a list | `tool_name: { in: [run_script, exec] }` |
| `matches` | Regular expression match | `path: { matches: "^/etc" }` |

### Conflict Strategy

When multiple rules match a tool call, `ConflictResolutionStrategy.DenyOverrides` applies: any matching `deny` rule wins regardless of `allow` rules. This is the default and recommended setting.

---

## Rate Limiting

Rate limiting uses ASP.NET's built-in fixed-window rate limiter with a policy named `"agent"`.

| Parameter | Value |
|-----------|-------|
| Window | 1 minute |
| Permit limit | Configurable via `Security:RateLimitPerMinute` (default: 30) |
| Queue limit | 0 (no queuing; excess requests are rejected immediately) |

When the limit is exceeded, the server returns HTTP 429 with a JSON body:

```json
{
  "error": "Rate limit exceeded. Try again later.",
  "retryAfterSeconds": 60
}
```

---

## Security Headers

Three security headers are applied to every HTTP response via inline middleware in `UseAiAgentCanvasSecurity()`:

| Header | Value | Purpose |
|--------|-------|---------|
| `X-Content-Type-Options` | `nosniff` | Prevents MIME-type sniffing |
| `X-Frame-Options` | `DENY` | Prevents the page from being embedded in frames |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Limits referrer information sent to other origins |

---

## Production Checklist

Before deploying to production, verify each item:

1. **Set real API keys** -- replace placeholder values in `AIFoundry:Endpoint` and `AIFoundry:Key` with production credentials, or enable `UseAzureCredential` for managed identity.
2. **Configure governance policies** -- review and customize `governance-policy.yaml` for your security requirements. Add deny rules for any tools that should not run in production.
3. **Enable rate limiting** -- set `Security:RateLimitPerMinute` to an appropriate value for your expected load.
4. **Review tool allowlists** -- audit the system tools configuration (`AllowedCommands`) and remove any commands not needed in production.
5. **Configure HTTPS** -- ensure the application runs behind HTTPS termination (reverse proxy or Kestrel HTTPS configuration).
6. **Set logging level** -- change `Logging:LogLevel:Default` to `Warning` or `Error` to reduce log volume and avoid logging sensitive data.
7. **Remove DevUI in production** -- the `AddDevUI()` and `MapDevUI()` calls should be wrapped in an environment check so the development UI is not exposed in production.
8. **Review MCP connection security** -- the default policy blocks MCP connections to private/internal addresses. Verify this rule is active and add additional endpoint restrictions as needed.
