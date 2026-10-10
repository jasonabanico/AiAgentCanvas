#pragma warning disable MEAI001

using AiAgentCanvas.Abstractions;
using AgentGovernance;
using AgentGovernance.Mcp;
using AgentGovernance.Policy;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Purview;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Security;

public static class SecurityServiceExtensions
{
    /// <summary>The name of the rate-limit policy the Host attaches to endpoints that spend model calls.</summary>
    public const string RateLimitPolicy = "agent";

    /// <summary>
    /// Side-effecting tools the platform ships. Override with
    /// <c>Security:ApprovalRequiredTools</c> when a deployment adds its own.
    /// </summary>
    private static readonly string[] DefaultApprovalRequiredTools =
    [
        "system_write_file",
        "system_run_script",
        "connect_mcp_server",
        "schedule_task",
    ];

    public static IServiceCollection AddAiAgentCanvasSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<GovernanceOptions>? configureGovernance = null,
        Action<McpGatewayConfig>? configureMcp = null)
    {
        var policyPath = configuration.GetValue<string>("Security:PolicyPath");
        var policyPaths = !string.IsNullOrEmpty(policyPath) && File.Exists(policyPath)
            ? new List<string> { policyPath }
            : new List<string>();

        var governanceOptions = new GovernanceOptions
        {
            EnableAudit = true,
            EnableMetrics = true,
            EnablePromptInjectionDetection = true,
            ConflictStrategy = ConflictResolutionStrategy.DenyOverrides,
            PolicyPaths = policyPaths,
        };

        configureGovernance?.Invoke(governanceOptions);

        GovernanceKernel kernel;
        try
        {
            kernel = new GovernanceKernel(governanceOptions);
        }
        catch (Exception)
        {
            kernel = new GovernanceKernel(new GovernanceOptions
            {
                EnableAudit = governanceOptions.EnableAudit,
                EnableMetrics = governanceOptions.EnableMetrics,
                EnablePromptInjectionDetection = governanceOptions.EnablePromptInjectionDetection,
                ConflictStrategy = governanceOptions.ConflictStrategy,
                PolicyPaths = new List<string>(),
            });
        }
        services.AddSingleton(kernel);
        services.AddSingleton(kernel.PolicyEngine);
        services.AddSingleton(kernel.AuditEmitter);

        services.AddSingleton<AIContextProvider, GovernanceContextProvider>();

        // These must be the names the tools are actually registered under. An entry
        // that matches no registered tool silently protects nothing.
        var approvalRequired = configuration
            .GetSection("Security:ApprovalRequiredTools")
            .Get<string[]>() ?? DefaultApprovalRequiredTools;

        var mcpConfig = new McpGatewayConfig
        {
            BlockOnSuspiciousPayload = true,
            ApprovalRequiredTools = approvalRequired.ToList(),
        };
        configureMcp?.Invoke(mcpConfig);
        services.AddSingleton(mcpConfig);
        services.AddSingleton<GovernedMcpGateway>();
        services.AddSingleton<IToolGovernanceWrapper, GovernanceToolWrapper>();

        // Requests per minute for each caller. Zero or less turns the limit off.
        var rateLimitPerMinute = configuration.GetValue("Security:RateLimitPerMinute", 30);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // One bucket for each caller. A single shared bucket would let one client use up
            // everyone's allowance.
            options.AddPolicy(RateLimitPolicy, context =>
                rateLimitPerMinute <= 0
                    ? RateLimitPartition.GetNoLimiter("unlimited")
                    : RateLimitPartition.GetFixedWindowLimiter(CallerKey(context), _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            options.OnRejected = async (context, ct) =>
            {
                var logger = context.HttpContext.RequestServices.GetService<ILogger<GovernanceKernel>>();
                logger?.LogWarning("[GOVERNANCE:RATE_LIMIT] Request rejected for {Caller}",
                    CallerKey(context.HttpContext));

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync(
                    """{"error":"Rate limit exceeded. Try again later."}""", ct);
            };
        });

        return services;
    }

    public static IServiceCollection AddAiAgentCanvasPurview(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var purviewSection = configuration.GetSection("Purview");
        if (!purviewSection.Exists() || string.IsNullOrEmpty(purviewSection["AppName"]))
            return services;

        var settings = new PurviewSettings(purviewSection["AppName"]!)
        {
            AppVersion = purviewSection["AppVersion"],
            TenantId = purviewSection["TenantId"],
        };

        var credential = new DefaultAzureCredential();

        services.AddSingleton(settings);
        services.AddSingleton(sp =>
            PurviewExtensions.PurviewAgentMiddleware(credential, settings,
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("AiAgentCanvas.Purview")));

        return services;
    }

    /// <summary>
    /// The caller an allowance belongs to: the authenticated identity when there is one, and
    /// the remote address otherwise. Behind a reverse proxy every request shares the proxy's
    /// address unless forwarded headers are configured, so configure them there.
    /// </summary>
    internal static string CallerKey(HttpContext context) =>
        context.User.Identity is { IsAuthenticated: true, Name: { Length: > 0 } name }
            ? $"user:{name}"
            : $"ip:{context.Connection.RemoteIpAddress}";

    /// <summary>
    /// Limits the endpoints that carry the <see cref="RateLimitPolicy"/> policy. It must run
    /// after authentication so the limit can tell callers apart.
    /// </summary>
    public static WebApplication UseAiAgentCanvasRateLimiting(this WebApplication app)
    {
        app.UseRateLimiter();
        return app;
    }

    /// <summary>Attaches the per-caller rate limit to an endpoint that spends model calls.</summary>
    public static TBuilder RequireAgentRateLimit<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireRateLimiting(RateLimitPolicy);

    public static WebApplication UseAiAgentCanvasSecurity(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            await next();
        });

        var kernel = app.Services.GetRequiredService<GovernanceKernel>();
        var logger = app.Services.GetRequiredService<ILogger<GovernanceKernel>>();

        kernel.OnAllEvents(e =>
        {
            logger.LogInformation("[GOVERNANCE:AUDIT] Type={Type} Agent={Agent} Policy={Policy}",
                e.Type, e.AgentId, e.PolicyName ?? "none");
        });

        return app;
    }
}
