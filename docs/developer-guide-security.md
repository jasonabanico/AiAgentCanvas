> [Developer Guide](developer-guide.md) > Security

# Developer Guide: Security

The `AiAgentCanvas.Security` project integrates Microsoft.AgentGovernance 4.0.0 for policy-based tool governance, prompt injection detection, and MCP gateway control. It also configures ASP.NET rate limiting and security headers.

## Security Overview

Security is registered first in `Program.cs`, before any other service, so that governance is in place before tools are registered:

```csharp
builder.Services.AddAiAgentCanvasSecurity(builder.Configuration);
// ... all other registrations ...
app.UseAiAgentCanvasSecurity();
```

### What Gets Registered

| Component | Purpose |
|-----------|---------|
| `GovernanceKernel` | Central governance object with policy engine and audit |
| `PolicyEngine` | Evaluates YAML rules against tool calls |
| `AuditEmitter` | Logs all governance events |
| `GovernanceContextProvider` | Scans for prompt injection before each LLM call |
| `GovernedMcpGateway` | Evaluates tool calls against policy for allow/deny decisions |
| `GovernedAIFunction` | DelegatingAIFunction wrapper that runs governance checks before each tool call |
| `GovernanceToolWrapper` | Implements `IToolGovernanceWrapper` (from Abstractions) to wrap all tools at startup |
| Rate limiter | Fixed-window rate limiting on the `/api/copilotkit` endpoint |
| Security headers | X-Content-Type-Options, X-Frame-Options, Referrer-Policy |

## AddAiAgentCanvasSecurity()

The registration method accepts optional configuration callbacks for governance options and MCP gateway config:

```csharp
public static IServiceCollection AddAiAgentCanvasSecurity(
    this IServiceCollection services,
    IConfiguration configuration,
    Action<GovernanceOptions>? configureGovernance = null,
    Action<McpGatewayConfig>? configureMcp = null)
{
    var governanceOptions = new GovernanceOptions
    {
        EnableAudit = true,
        EnableMetrics = true,
        EnablePromptInjectionDetection = true,
        ConflictStrategy = ConflictResolutionStrategy.DenyOverrides,
        PolicyPaths = policyPaths,
    };

    configureGovernance?.Invoke(governanceOptions);

    var kernel = new GovernanceKernel(governanceOptions);
    services.AddSingleton(kernel);
    services.AddSingleton(kernel.PolicyEngine);
    services.AddSingleton(kernel.AuditEmitter);
    // ...
}
```

### GovernanceOptions Defaults

| Option | Default | Description |
|--------|---------|-------------|
| `EnableAudit` | `true` | Log all governance decisions |
| `EnableMetrics` | `true` | Collect governance metrics |
| `EnablePromptInjectionDetection` | `true` | Scan system instructions for injection |
| `ConflictStrategy` | `DenyOverrides` | When rules conflict, deny wins |
| `PolicyPaths` | From config | Path to `governance-policy.yaml` |

## GovernanceKernel

The `GovernanceKernel` is the central governance object. It is constructed from `GovernanceOptions` and provides access to the `PolicyEngine`, `AuditEmitter`, and `InjectionDetector`.

During startup, `UseAiAgentCanvasSecurity()` subscribes to all governance events for logging:

```csharp
kernel.OnAllEvents(e =>
{
    logger.LogInformation("[GOVERNANCE:AUDIT] Type={Type} Agent={Agent} Policy={Policy}",
        e.Type, e.AgentId, e.PolicyName ?? "none");
});
```

## GovernanceContextProvider

An `AIContextProvider` that runs before each LLM call to scan system instructions for prompt injection attempts.

```csharp
public sealed class GovernanceContextProvider : AIContextProvider
{
    private readonly GovernanceKernel _kernel;

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken)
    {
        if (_kernel.InjectionDetector is null ||
            string.IsNullOrEmpty(context.AIContext.Instructions))
            return new ValueTask<AIContext>(context.AIContext);

        var result = _kernel.InjectionDetector.Detect(context.AIContext.Instructions);
        if (result.IsInjection)
        {
            _logger.LogWarning("[GOVERNANCE] Prompt injection detected: {Type}",
                result.InjectionType);

            _kernel.AuditEmitter.Emit(
                GovernanceEventType.PolicyViolation,
                "system", "instructions",
                new Dictionary<string, object>
                {
                    ["injectionType"] = result.InjectionType.ToString(),
                    ["source"] = "system_instructions",
                });
        }

        return new ValueTask<AIContext>(context.AIContext);
    }
}
```

This catches injection attempts that may have been introduced through user-managed data (personas, context entries, etc.) before they reach the LLM.

## GovernedMcpGateway

Wraps the AgentGovernance `McpGateway` to evaluate tool calls against governance policies. It checks whether a tool call should be allowed, denied, or requires approval.

```csharp
public sealed class GovernedMcpGateway
{
    private readonly McpGateway _gateway;

    public GovernedMcpGateway(McpGatewayConfig config, ILogger<GovernedMcpGateway> logger)
    {
        _gateway = new McpGateway(config);
    }

    public McpGatewayDecision Evaluate(string agentId, string toolName, string? payload = null)
    {
        var request = new McpGatewayRequest
        {
            AgentId = agentId,
            ToolName = toolName,
            Payload = payload ?? "",
        };

        var decision = _gateway.ProcessRequest(request);

        if (!decision.Allowed)
            _logger.LogWarning("[GOVERNANCE:MCP] Blocked tool={Tool} agent={Agent}",
                toolName, agentId);

        return decision;
    }
}
```

### McpGatewayConfig

```csharp
var mcpConfig = new McpGatewayConfig
{
    BlockOnSuspiciousPayload = true,
    ApprovalRequiredTools = ["run_script", "write_file"],
};
```

| Option | Default | Description |
|--------|---------|-------------|
| `BlockOnSuspiciousPayload` | `true` | Block tools with suspicious payloads |
| `ApprovalRequiredTools` | `["run_script", "write_file"]` | Tools that require explicit approval |

## Tool-Call Governance

Every tool registered at startup is wrapped with governance checks via the `IToolGovernanceWrapper` abstraction. This ensures that **every** tool call goes through policy evaluation, not just MCP calls.

### How It Works

1. The Security project registers `GovernanceToolWrapper` as `IToolGovernanceWrapper`
2. Core's `AddAiAgentCanvas()` optionally resolves `IToolGovernanceWrapper` from DI
3. If present, each `AIFunction` is wrapped in a `GovernedAIFunction` (a `DelegatingAIFunction`)
4. Before every tool call, `GovernedAIFunction.InvokeCoreAsync()` runs `GovernedMcpGateway.Evaluate()`
5. Audit events are emitted for both allowed and blocked calls
6. Blocked calls return an error JSON to the LLM instead of executing

This is opt-in: if Security is not registered, tools pass through unwrapped. The `IToolGovernanceWrapper` interface lives in Abstractions so Core never references Security directly.

### GovernedAIFunction

```csharp
public sealed class GovernedAIFunction : DelegatingAIFunction
{
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var decision = _gateway.Evaluate("agent", toolName, payload);

        _auditEmitter.Emit(
            decision.Allowed ? GovernanceEventType.PolicyCheck
                             : GovernanceEventType.ToolCallBlocked,
            "agent", toolName, ...);

        if (!decision.Allowed)
            return /* error JSON */;

        return await base.InvokeCoreAsync(arguments, cancellationToken);
    }
}
```

## Governance Policy (YAML)

Policies are defined in `governance-policy.yaml` and evaluated by the `PolicyEngine`. Each rule has a name, scope, action, conditions, and reason.

```yaml
name: AiAgentCanvas-default
description: Default governance policy for AiAgentCanvas

rules:
  - name: block-dangerous-tools
    scope: tool_call
    action: deny
    conditions:
      tool_name:
        in: [run_script]
    reason: "Shell execution requires explicit approval"

  - name: restrict-file-write-paths
    scope: tool_call
    action: deny
    conditions:
      tool_name:
        equals: write_file
      path:
        matches: "^(/etc|/var|C:\\\\Windows|C:\\\\Program Files)"
    reason: "Writing to system directories is blocked"

  - name: block-private-mcp-endpoints
    scope: tool_call
    action: deny
    conditions:
      tool_name:
        equals: connect_mcp_server
      endpoint:
        matches: "(localhost|127\\.0\\.0\\.1|169\\.254\\.|10\\.|172\\.(1[6-9]|2[0-9]|3[01])\\.|192\\.168\\.)"
    reason: "MCP connections to private addresses are blocked (SSRF protection)"

  - name: allow-all-other
    scope: tool_call
    action: allow
    conditions: {}
```

### Rule Structure

| Field | Description |
|-------|-------------|
| `name` | Unique rule identifier |
| `scope` | What the rule applies to (`tool_call`) |
| `action` | `allow` or `deny` |
| `conditions` | Matching criteria: `equals`, `in`, `matches` (regex) |
| `reason` | Human-readable explanation logged on match |

Rules are evaluated in order. The `ConflictStrategy` of `DenyOverrides` means if any rule denies, the action is blocked regardless of subsequent allow rules.

### Configuration

The policy file path is configured in `appsettings.json`:

```json
{
  "Security": {
    "PolicyPath": "governance-policy.yaml",
    "RateLimitPerMinute": 30
  }
}
```

## ASP.NET Rate Limiting

A fixed-window rate limiter protects the agent endpoint:

```csharp
var rateLimitWindow = configuration.GetValue("Security:RateLimitPerMinute", 30);
services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("agent", limiter =>
    {
        limiter.PermitLimit = rateLimitWindow;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});
```

When the limit is exceeded, the response is:

```json
{"error": "Rate limit exceeded. Try again later."}
```

## Security Headers

`UseAiAgentCanvasSecurity()` adds the following headers to every response:

| Header | Value | Purpose |
|--------|-------|---------|
| `X-Content-Type-Options` | `nosniff` | Prevents MIME-type sniffing |
| `X-Frame-Options` | `DENY` | Prevents clickjacking |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Controls referrer information |

## Customizing Security

Override defaults by passing configuration callbacks:

```csharp
builder.Services.AddAiAgentCanvasSecurity(builder.Configuration,
    configureGovernance: options =>
    {
        options.EnablePromptInjectionDetection = true;
        options.ConflictStrategy = ConflictResolutionStrategy.AllowOverrides;
    },
    configureMcp: config =>
    {
        config.ApprovalRequiredTools = ["run_script", "write_file", "delete_file"];
        config.BlockOnSuspiciousPayload = true;
    });
```

---

