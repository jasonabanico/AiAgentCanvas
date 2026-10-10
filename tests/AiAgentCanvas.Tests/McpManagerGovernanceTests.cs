#pragma warning disable MEAI001

using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Capabilities.McpServer;
using AiAgentCanvas.Capabilities.Skills;
using AiAgentCanvas.Orchestration.Services;
using AiAgentCanvas.Orchestration.Skills;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiAgentCanvas.Tests;

/// <summary>
/// A server connected through connect_mcp_server must not get more trust than a tool
/// registered at startup. These tests connect the real manager to a real MCP server.
/// </summary>
public class McpManagerGovernanceTests
{
    private sealed class BlockEcho : IToolGovernanceWrapper
    {
        public AIFunction Wrap(AIFunction tool) => tool.Name == "echo" ? new Blocked(tool) : tool;

        private sealed class Blocked(AIFunction inner) : DelegatingAIFunction(inner)
        {
            protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) =>
                new("BLOCKED by policy");
        }
    }

    private static async Task<(WebApplication App, string Endpoint)> StartServerAsync()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agent:McpServer:ExposedTools:0"] = "echo",
            ["Agent:McpServer:ExposedTools:1"] = "ping_tool",
        }).Build();

        IReadOnlyList<AITool> tools =
        [
            AIFunctionFactory.Create((string text) => $"echo: {text}", "echo", "Repeats the text."),
            AIFunctionFactory.Create(() => "pong", "ping_tool", "Answers pong."),
        ];

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddLogging();
        builder.Services.AddSingleton(tools);
        builder.Services.AddAiAgentCanvasMcpServer(configuration);
        var app = builder.Build();
        app.MapAiAgentCanvasMcp(app.Services.GetRequiredService<McpServerOptions>());
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (app, address + "/mcp");
    }

    private static async Task<object?> CallAsync(DynamicToolRegistry registry, string name, Dictionary<string, object?>? args = null)
    {
        var tool = registry.GetAllTools().OfType<AIFunction>().Single(t => t.Name == name);
        return await tool.InvokeAsync(new AIFunctionArguments(args));
    }

    [Fact]
    public async Task A_tool_from_a_connected_server_passes_through_the_governance_policy()
    {
        var (app, endpoint) = await StartServerAsync();
        await using var _ = app;
        var services = new ServiceCollection().AddSingleton<IToolGovernanceWrapper>(new BlockEcho()).BuildServiceProvider();
        var registry = new DynamicToolRegistry();
        await using var manager = new McpConnectionManager(registry, NullLoggerFactory.Instance, services);

        await manager.ConnectAsync("srv", endpoint, "http");

        var blocked = await CallAsync(registry, "echo", new() { ["text"] = "hi" });
        var allowed = await CallAsync(registry, "ping_tool");
        Assert.Contains("BLOCKED", blocked!.ToString());
        Assert.Contains("pong", allowed!.ToString());
    }

    [Fact]
    public async Task A_tool_from_a_connected_server_is_wrapped_for_tracing()
    {
        var (app, endpoint) = await StartServerAsync();
        await using var _ = app;
        var services = new ServiceCollection().BuildServiceProvider();
        var registry = new DynamicToolRegistry();
        await using var manager = new McpConnectionManager(registry, NullLoggerFactory.Instance, services);

        await manager.ConnectAsync("srv", endpoint, "http");

        Assert.All(registry.GetAllTools(), t => Assert.IsType<TracedAIFunction>(t));
    }

    [Fact]
    public async Task Without_a_service_provider_the_tools_are_registered_as_they_come()
    {
        var (app, endpoint) = await StartServerAsync();
        await using var _ = app;
        var registry = new DynamicToolRegistry();
        await using var manager = new McpConnectionManager(registry, NullLoggerFactory.Instance);

        await manager.ConnectAsync("srv", endpoint, "http");

        Assert.DoesNotContain(registry.GetAllTools(), t => t is TracedAIFunction);
        Assert.Contains("echo: hi", (await CallAsync(registry, "echo", new() { ["text"] = "hi" }))!.ToString());
    }
}
