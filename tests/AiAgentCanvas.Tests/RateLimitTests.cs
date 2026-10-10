using System.Net;
using System.Security.Claims;
using AiAgentCanvas.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AiAgentCanvas.Tests;

/// <summary>
/// The rate limit has to apply to the endpoints that carry it, count each caller on their own,
/// and leave other endpoints alone. The pipeline order here matches the Host's.
/// </summary>
public class RateLimitTests
{
    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(int limit)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:RateLimitPerMinute"] = limit.ToString(),
        }).Build();

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddLogging();
        builder.Services.AddAiAgentCanvasSecurity(configuration);
        var app = builder.Build();

        app.UseAiAgentCanvasSecurity();

        // Stands in for authentication: the header names the caller.
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.TryGetValue("X-Test-User", out var user))
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, user.ToString())], authenticationType: "test"));
            }
            await next();
        });

        app.UseAiAgentCanvasRateLimiting();

        app.MapGet("/limited", () => "ok").RequireAgentRateLimit();
        app.MapGet("/open", () => "ok");
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (app, new HttpClient { BaseAddress = new Uri(address) });
    }

    private static Task<HttpResponseMessage> Get(HttpClient client, string path, string? user = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (user is not null)
            request.Headers.Add("X-Test-User", user);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Requests_inside_the_limit_pass_and_the_next_one_is_refused_with_a_json_body_and_a_retry_hint()
    {
        var (app, client) = await StartAsync(limit: 3);
        await using var _ = app;

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await Get(client, "/limited")).StatusCode);

        var refused = await Get(client, "/limited");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Contains("Rate limit exceeded", await refused.Content.ReadAsStringAsync());
        Assert.Contains("application/json", refused.Content.Headers.ContentType!.ToString());
        Assert.True(refused.Headers.RetryAfter?.Delta is { TotalSeconds: > 0 and <= 60 });
    }

    [Fact]
    public async Task An_endpoint_without_the_policy_is_not_limited()
    {
        var (app, client) = await StartAsync(limit: 2);
        await using var _ = app;

        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await Get(client, "/open")).StatusCode);
    }

    [Fact]
    public async Task Each_signed_in_caller_has_an_allowance_of_their_own()
    {
        var (app, client) = await StartAsync(limit: 2);
        await using var _ = app;

        await Get(client, "/limited", "alice");
        await Get(client, "/limited", "alice");
        var aliceOver = await Get(client, "/limited", "alice");
        var bob = await Get(client, "/limited", "bob");

        Assert.Equal(HttpStatusCode.TooManyRequests, aliceOver.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bob.StatusCode);
    }

    [Fact]
    public async Task Callers_who_have_not_signed_in_share_an_allowance_by_address()
    {
        var (app, client) = await StartAsync(limit: 2);
        await using var _ = app;

        await Get(client, "/limited");
        await Get(client, "/limited");

        Assert.Equal(HttpStatusCode.TooManyRequests, (await Get(client, "/limited")).StatusCode);
    }

    [Fact]
    public async Task A_signed_in_caller_is_not_held_back_by_anonymous_traffic_from_the_same_address()
    {
        var (app, client) = await StartAsync(limit: 2);
        await using var _ = app;
        await Get(client, "/limited");
        await Get(client, "/limited");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Get(client, "/limited")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Get(client, "/limited", "carol")).StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_limit_of_zero_or_less_turns_the_limit_off(int limit)
    {
        var (app, client) = await StartAsync(limit);
        await using var _ = app;

        for (var i = 0; i < 20; i++)
            Assert.Equal(HttpStatusCode.OK, (await Get(client, "/limited")).StatusCode);
    }
}
