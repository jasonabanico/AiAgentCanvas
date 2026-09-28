using AiAgentCanvas.Authentication.Schemes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Authentication;

public static class AuthenticationServiceExtensions
{
    /// <summary>Policy every protected endpoint uses.</summary>
    public const string AgentPolicy = "AiAgentCanvas";

    /// <summary>
    /// Registers the schemes named in <c>Authentication:Schemes</c>. Add your own
    /// <see cref="IAgentAuthenticationScheme"/> to the container before calling this
    /// and it becomes selectable by name alongside the built-in ones.
    /// </summary>
    public static IServiceCollection AddAiAgentCanvasAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new AgentAuthenticationOptions();
        configuration.GetSection(AgentAuthenticationOptions.SectionName).Bind(options);

        // Bind appends to a List<T> that already has defaults rather than replacing
        // it, so a configured AllowAnonymous would otherwise stack on top of ours.
        options.AllowAnonymous = options.AllowAnonymous.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        options.Schemes = options.Schemes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        options.AllowedOrigins = options.AllowedOrigins.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        services.AddSingleton(options);

        services.AddSingleton<IAgentAuthenticationScheme, ApiKeyScheme>();
        services.AddSingleton<IAgentAuthenticationScheme, JwtBearerScheme>();

        if (!options.Enabled)
        {
            // Authorization still has to resolve, because endpoints reference the
            // policy unconditionally. With authentication off the policy asserts
            // nothing, which keeps a fresh clone runnable.
            services.AddAuthorization(auth =>
                auth.AddPolicy(AgentPolicy, policy => policy.RequireAssertion(_ => true)));
            return services;
        }

        var available = services.BuildServiceProvider().GetServices<IAgentAuthenticationScheme>().ToList();
        var selected = ResolveSchemes(options, available);

        var builder = services.AddAuthentication(selected[0].Name);
        foreach (var scheme in selected)
            scheme.Register(builder, options);

        services.AddAuthorization(auth =>
            auth.AddPolicy(AgentPolicy, policy =>
            {
                policy.AddAuthenticationSchemes(selected.Select(s => s.Name).ToArray());
                policy.RequireAuthenticatedUser();

                if (options.JwtBearer.RequiredScopes.Count > 0
                    && selected.Any(s => s.Name == JwtBearerScheme.SchemeName))
                {
                    policy.RequireAssertion(context =>
                    {
                        // An API key caller carries no scopes, so scope enforcement
                        // applies only to tokens that came through JwtBearer.
                        var viaApiKey = context.User.HasClaim("auth_scheme", ApiKeyScheme.SchemeName);
                        if (viaApiKey) return true;

                        var scope = context.User.FindFirst("scp")?.Value
                            ?? context.User.FindFirst("http://schemas.microsoft.com/identity/claims/scope")?.Value;
                        return JwtBearerScheme.HasRequiredScopes(options.JwtBearer.RequiredScopes, scope);
                    });
                }
            }));

        return services;
    }

    private static List<IAgentAuthenticationScheme> ResolveSchemes(
        AgentAuthenticationOptions options,
        List<IAgentAuthenticationScheme> available)
    {
        if (options.Schemes.Count == 0)
        {
            throw new InvalidOperationException(
                "Authentication:Enabled is true but Authentication:Schemes is empty. "
                + $"Choose one or more of: {string.Join(", ", available.Select(s => s.Name))}.");
        }

        var selected = new List<IAgentAuthenticationScheme>();
        foreach (var name in options.Schemes)
        {
            var match = available.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw new InvalidOperationException(
                    $"Unknown authentication scheme '{name}'. "
                    + $"Available: {string.Join(", ", available.Select(s => s.Name))}.");
            }
            selected.Add(match);
        }

        return selected;
    }

    /// <summary>
    /// Adds the authentication and authorization middleware. Call before the agent
    /// endpoints are mapped.
    /// </summary>
    public static WebApplication UseAiAgentCanvasAuthentication(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<AgentAuthenticationOptions>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AiAgentCanvas.Authentication");

        if (!options.Enabled)
        {
            logger.LogWarning(
                "[SECURITY] Authentication is DISABLED. The AG-UI, A2A, DevUI, notification and webhook "
                + "endpoints are open to anyone who can reach this process, which runs shell commands and "
                + "schedules unattended work. Set Authentication:Enabled and choose a scheme before exposing it.");
            return app;
        }

        app.UseAuthentication();
        app.UseAuthorization();

        logger.LogInformation(
            "Authentication enabled. Schemes: {Schemes}. Anonymous endpoints: {Anonymous}",
            string.Join(", ", options.Schemes),
            options.AllowAnonymous.Count > 0 ? string.Join(", ", options.AllowAnonymous) : "(none)");

        return app;
    }

    /// <summary>
    /// Applies the agent policy to an endpoint unless its key is listed in
    /// <c>Authentication:AllowAnonymous</c>. Endpoints call this rather than
    /// RequireAuthorization directly so the allow list is honoured in one place.
    /// </summary>
    public static TBuilder RequireAgentAuthorization<TBuilder>(
        this TBuilder builder,
        AgentAuthenticationOptions options,
        string endpointKey) where TBuilder : IEndpointConventionBuilder
    {
        if (!options.Enabled || options.IsAnonymous(endpointKey))
            return builder;

        builder.RequireAuthorization(AgentPolicy);
        return builder;
    }
}
