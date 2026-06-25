using AiAgentCanvas.Abstractions;
using AgentGovernance;
using AgentGovernance.Mcp;
using AgentGovernance.Policy;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Security;

public static class SecurityServiceExtensions
{
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

        var mcpConfig = new McpGatewayConfig
        {
            BlockOnSuspiciousPayload = true,
            ApprovalRequiredTools = ["run_script", "write_file"],
        };
        configureMcp?.Invoke(mcpConfig);
        services.AddSingleton(mcpConfig);
        services.AddSingleton<GovernedMcpGateway>();
        services.AddSingleton<IToolGovernanceWrapper, GovernanceToolWrapper>();

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
            options.OnRejected = async (context, ct) =>
            {
                var logger = context.HttpContext.RequestServices.GetService<ILogger<GovernanceKernel>>();
                logger?.LogWarning("[GOVERNANCE:RATE_LIMIT] Request rejected from {IP}",
                    context.HttpContext.Connection.RemoteIpAddress);

                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync(
                    """{"error":"Rate limit exceeded. Try again later."}""", ct);
            };
        });

        return services;
    }

    public static WebApplication UseAiAgentCanvasSecurity(this WebApplication app)
    {
        app.UseRateLimiter();

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
