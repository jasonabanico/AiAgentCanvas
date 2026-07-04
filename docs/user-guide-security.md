> [User Guide](user-guide.md) > Security

# User Guide: Security

AI Agent Canvas includes a layered security system covering governance policies, rate limiting, prompt injection detection, tool-call rules, security headers, and audit logging.

## Governance Policy Overview

The governance policy defines rules that control what the agent can and cannot do. Rules are defined in a YAML file (default: `governance-policy.yaml` in the web project root) and enforced by the `GovernanceKernel` at runtime.

The default policy includes these rules:

| Rule | Action | Description |
|------|--------|-------------|
| `block-dangerous-tools` | Deny | Blocks execution of the `run_script` tool. |
| `restrict-file-write-paths` | Deny | Prevents `write_file` from targeting system directories (`/etc`, `/var`, `C:\Windows`, `C:\Program Files`). |
| `block-private-mcp-endpoints` | Deny | Blocks `connect_mcp_server` from connecting to private or internal network addresses (SSRF protection). |
| `limit-scheduled-tasks` | Deny | Blocks scheduling tools when the rate limit is exceeded. |
| `allow-all-other` | Allow | Permits all tool calls not matched by a deny rule. |

### Policy File Format

```yaml
name: AiAgentCanvas-default
rules:
  - name: block-dangerous-tools
    action: deny
    tools:
      - run_script
    description: Block script execution

  - name: restrict-file-write-paths
    action: deny
    tools:
      - write_file
    paths:
      - /etc
      - /var
      - C:\Windows
      - C:\Program Files
    description: Restrict file writes to safe locations

  - name: allow-all-other
    action: allow
    description: Permit everything else
```

### Customizing the Policy

Edit `governance-policy.yaml` to add, modify, or remove rules. The policy path is configured in `appsettings.json`:

```json
{
  "Security": {
    "PolicyPath": "governance-policy.yaml"
  }
}
```

The conflict resolution strategy is **deny overrides** -- if any rule denies a tool call, it is blocked regardless of other allow rules.

## Rate Limiting

The backend enforces a fixed-window rate limit on all API requests. When a client exceeds the limit, the server responds with **HTTP 429 Too Many Requests**.

Configure the limit in `appsettings.json`:

```json
{
  "Security": {
    "RateLimitPerMinute": 30
  }
}
```

The default is 30 requests per minute. Increase this for high-throughput environments or decrease it to conserve API quota.

## Prompt Injection Detection

The `GovernanceKernel` includes built-in prompt injection detection. When enabled, it scans incoming messages for patterns that attempt to override the agent's instructions or extract system prompts.

This feature is enabled by default (`EnablePromptInjectionDetection: true`). Suspicious messages are flagged in the audit log and may be blocked depending on the governance configuration.

## Tool-Call Governance

Every tool call passes through the governance pipeline before execution. The pipeline evaluates the call against all active policy rules:

1. The agent decides to call a tool (e.g., `connect_mcp_server`).
2. The governance kernel evaluates the call against deny rules.
3. If any deny rule matches, the call is blocked and the agent receives an error.
4. If no deny rule matches, the call proceeds.

### Governed MCP Gateway

MCP connections receive additional scrutiny through the `GovernedMcpGateway`:

- **Suspicious payload blocking** -- Payloads that contain potential injection patterns are blocked (`BlockOnSuspiciousPayload: true`).
- **Approval-required tools** -- Certain tools (`run_script`, `write_file`) require explicit approval before execution.

## Guardrails (User-Defined Safety Rules)

In addition to the system-level governance policy, you can create user-defined guardrail rules through conversation. Guardrails are injected into the agent's system prompt and guide its behavior.

### Creating a Guardrail

```
Create a guardrail called "No Investment Advice" with severity "critical"
and rule "Never provide specific investment recommendations or buy/sell
signals. Always include a disclaimer that this is not financial advice."
```

### Managing Guardrails

```
List my guardrails
Toggle the "No Investment Advice" guardrail off
Update the "No Investment Advice" guardrail to also prohibit price predictions
Delete the "No Investment Advice" guardrail
```

Guardrail severities:

| Severity | Purpose |
|----------|---------|
| `critical` | Rules the agent must never violate. |
| `warning` | Rules the agent should follow but may deviate from with justification. |
| `info` | Guidance that shapes behavior but does not restrict it. |

Guardrails are stored as markdown files in `agent-data/guardrails/` and injected into the system prompt by the `GuardrailContextProvider`.

## Security Headers

The backend adds the following security headers to all HTTP responses:

| Header | Value | Purpose |
|--------|-------|---------|
| `X-Content-Type-Options` | `nosniff` | Prevents MIME-type sniffing. |
| `X-Frame-Options` | `DENY` | Prevents the page from being embedded in frames (clickjacking protection). |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Controls referrer information sent with requests. |

These headers are applied automatically and require no configuration.

## Audit Logging

The governance system logs all tool calls and policy evaluations. Audit logging is enabled by default (`EnableAudit: true`) and records:

- Every tool call made by the agent
- The governance rules evaluated
- Whether the call was allowed or denied
- Timestamps and request details

Audit logs appear in the standard .NET logging output. Configure the log level in `appsettings.json` to control verbosity:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

Set to `Debug` for detailed governance evaluation traces.

## Security Checklist for Production

- Replace the `AIFoundry.Key` with a Managed Identity (`UseAzureCredential: true`).
- Verify Yahoo Finance and SEC EDGAR connectivity from the production network.
- Review and customize `governance-policy.yaml` for your use case.
- Add authentication to the Hangfire dashboard (`/hangfire`).
- Adjust `RateLimitPerMinute` for your expected load.
- Set `AllowedHosts` to your specific domain instead of `"*"`.
- Enable HTTPS and configure appropriate TLS settings.
- Review audit logs regularly for unusual tool call patterns.

---

> **[Download the complete PDF guide](guides/AI-Agent-Canvas-Guide.pdf)** | **[AI-First Company Guide](guides/AI-First-Company-Guide.pdf)**
