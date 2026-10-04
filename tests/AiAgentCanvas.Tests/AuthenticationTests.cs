using AiAgentCanvas.Authentication;
using AiAgentCanvas.Authentication.Schemes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AiAgentCanvas.Tests;

public class AuthenticationTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Disabled_by_default_so_a_fresh_clone_runs()
    {
        var options = new AgentAuthenticationOptions();
        Assert.False(options.Enabled);
        Assert.Contains("health", options.AllowAnonymous);
    }

    [Fact]
    public void Health_stays_anonymous_but_other_endpoints_do_not()
    {
        var options = new AgentAuthenticationOptions();
        Assert.True(options.IsAnonymous("health"));
        Assert.False(options.IsAnonymous("agui"));
        Assert.False(options.IsAnonymous("a2a"));
        Assert.False(options.IsAnonymous("webhooks"));
    }

    [Fact]
    public void Binding_does_not_stack_configured_values_on_top_of_defaults()
    {
        // Bind appends to a non-empty List<T>, which produced "health, health"
        // before the dedupe.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAiAgentCanvasAuthentication(Config(new()
        {
            ["Authentication:Enabled"] = "false",
            ["Authentication:AllowAnonymous:0"] = "health",
            ["Authentication:AllowAnonymous:1"] = "devui",
        }));

        var options = services.BuildServiceProvider().GetRequiredService<AgentAuthenticationOptions>();

        Assert.Equal(2, options.AllowAnonymous.Count);
        Assert.Equal(["health", "devui"], options.AllowAnonymous);
    }

    [Fact]
    public void Enabling_without_choosing_a_scheme_fails_loudly()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddAiAgentCanvasAuthentication(Config(new()
            {
                ["Authentication:Enabled"] = "true",
            })));

        Assert.Contains("Schemes is empty", ex.Message);
    }

    [Fact]
    public void Unknown_scheme_name_fails_loudly_and_lists_the_options()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddAiAgentCanvasAuthentication(Config(new()
            {
                ["Authentication:Enabled"] = "true",
                ["Authentication:Schemes:0"] = "Kerberos",
            })));

        Assert.Contains("Unknown authentication scheme 'Kerberos'", ex.Message);
        Assert.Contains("ApiKey", ex.Message);
        Assert.Contains("JwtBearer", ex.Message);
    }

    [Fact]
    public void JwtBearer_without_an_authority_fails_loudly()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddAiAgentCanvasAuthentication(Config(new()
            {
                ["Authentication:Enabled"] = "true",
                ["Authentication:Schemes:0"] = "JwtBearer",
            })));

        Assert.Contains("Authority is required", ex.Message);
    }

    [Fact]
    public void Api_key_scheme_registers_without_extra_configuration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAiAgentCanvasAuthentication(Config(new()
        {
            ["Authentication:Enabled"] = "true",
            ["Authentication:Schemes:0"] = "ApiKey",
            ["Authentication:ApiKey:Keys:0"] = "secret",
        }));

        var options = services.BuildServiceProvider().GetRequiredService<AgentAuthenticationOptions>();

        Assert.True(options.Enabled);
        Assert.Equal(["ApiKey"], options.Schemes);
        Assert.Equal(["secret"], options.ApiKey.Keys);
    }

    [Fact]
    public void Both_schemes_can_run_together()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAiAgentCanvasAuthentication(Config(new()
        {
            ["Authentication:Enabled"] = "true",
            ["Authentication:Schemes:0"] = "JwtBearer",
            ["Authentication:Schemes:1"] = "ApiKey",
            ["Authentication:JwtBearer:Authority"] = "https://login.microsoftonline.com/common/v2.0",
            ["Authentication:JwtBearer:Audience"] = "api://agent",
            ["Authentication:ApiKey:Keys:0"] = "secret",
        }));

        var options = services.BuildServiceProvider().GetRequiredService<AgentAuthenticationOptions>();
        Assert.Equal(["JwtBearer", "ApiKey"], options.Schemes);
    }

    [Theory]
    [InlineData("agent.read agent.write", new[] { "agent.read" }, true)]
    [InlineData("agent.read", new[] { "agent.read", "agent.write" }, false)]
    [InlineData("AGENT.READ", new[] { "agent.read" }, true)]
    [InlineData("", new[] { "agent.read" }, false)]
    [InlineData(null, new[] { "agent.read" }, false)]
    public void Scope_check_requires_every_listed_scope(string? granted, string[] required, bool expected) =>
        Assert.Equal(expected, JwtBearerScheme.HasRequiredScopes(required, granted));

    [Fact]
    public void No_required_scopes_accepts_any_valid_token()
    {
        Assert.True(JwtBearerScheme.HasRequiredScopes([], null));
        Assert.True(JwtBearerScheme.HasRequiredScopes([], "anything"));
    }
}
