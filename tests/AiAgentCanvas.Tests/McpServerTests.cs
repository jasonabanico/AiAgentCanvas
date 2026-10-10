#pragma warning disable MEAI001

using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Capabilities.McpServer;
using AiAgentCanvas.Capabilities.RunLedger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using Xunit;

namespace AiAgentCanvas.Tests;

public class McpServerTests : IDisposable
{
    private readonly string _ledgerPath = Path.Combine(Path.GetTempPath(), $"mcpserver-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_ledgerPath + suffix); } catch (IOException) { }
        }
    }

    private static IReadOnlyList<AITool> HostTools() =>
    [
        AIFunctionFactory.Create((string text) => $"echo: {text}", "echo", "Repeats the text."),
        AIFunctionFactory.Create(() => "secret internal data", "internal_secret", "Not for outside callers."),
        AIFunctionFactory.Create(() => "one", "fam_one", "Family member one."),
        AIFunctionFactory.Create(() => "two", "fam_two", "Family member two."),
        AIFunctionFactory.Create(() => new string('x', 500), "long_result", "Returns a long string."),
        AIFunctionFactory.Create(() => string.Empty, "other", "Another tool."),
        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "sent", "send_it", "Needs a person to approve.")),
    ];

    private async Task<(WebApplication App, McpClient Client, SqliteRunLedger Ledger)> StartAsync(
        Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var ledger = new SqliteRunLedger(_ledgerPath, 500);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddLogging();
        builder.Services.AddSingleton(HostTools());
        builder.Services.AddSingleton<IRunLedger>(ledger);
        builder.Services.AddAiAgentCanvasMcpServer(configuration);
        var app = builder.Build();
        app.MapAiAgentCanvasMcp(app.Services.GetRequiredService<McpServerOptions>());
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(address + "/mcp") });
        var client = await McpClient.CreateAsync(transport);
        return (app, client, ledger);
    }

    private static Dictionary<string, string?> Exposed(params string[] names)
    {
        var settings = new Dictionary<string, string?>();
        for (var i = 0; i < names.Length; i++)
            settings[$"Agent:McpServer:ExposedTools:{i}"] = names[i];
        return settings;
    }

    [Fact]
    public async Task An_outside_client_sees_only_the_tools_named_in_configuration()
    {
        var (app, client, _) = await StartAsync(Exposed("echo", "fam_*"));
        await using var _a = app;
        await using var _c = client;

        var tools = (await client.ListToolsAsync()).Select(t => t.Name).OrderBy(n => n).ToList();

        Assert.Equal(["echo", "fam_one", "fam_two"], tools);
    }

    [Fact]
    public async Task With_nothing_configured_nothing_is_exposed()
    {
        var (app, client, _) = await StartAsync([]);
        await using var _a = app;
        await using var _c = client;

        Assert.Empty(await client.ListToolsAsync());
    }

    [Fact]
    public async Task A_tool_that_needs_approval_is_never_offered_even_when_named()
    {
        var (app, client, _) = await StartAsync(Exposed("echo", "send_it"));
        await using var _a = app;
        await using var _c = client;

        var tools = (await client.ListToolsAsync()).Select(t => t.Name).ToList();

        Assert.Equal(["echo"], tools);
    }

    [Fact]
    public async Task A_client_can_call_an_exposed_tool_and_gets_the_result()
    {
        var (app, client, _) = await StartAsync(Exposed("echo"));
        await using var _a = app;
        await using var _c = client;

        var result = await client.CallToolAsync("echo", new Dictionary<string, object?> { ["text"] = "hello" });

        Assert.Contains("echo: hello", result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Single().Text);
    }

    [Fact]
    public async Task A_tool_that_was_not_exposed_cannot_be_called_by_name()
    {
        var (app, client, _) = await StartAsync(Exposed("echo"));
        await using var _a = app;
        await using var _c = client;

        await Assert.ThrowsAnyAsync<Exception>(() => client.CallToolAsync("internal_secret", new Dictionary<string, object?>()).AsTask());
    }

    [Fact]
    public async Task Each_outside_call_is_recorded_in_the_run_ledger_as_an_external_run()
    {
        var (app, client, ledger) = await StartAsync(Exposed("echo"));
        await using var _a = app;
        await using var _c = client;

        await client.CallToolAsync("echo", new Dictionary<string, object?> { ["text"] = "audit me" });

        var recorded = Assert.Single(ledger.Recent(new RunQuery(Limit: 10)));
        Assert.Equal(RunSource.External, recorded.Source);
        Assert.Equal(RunStatus.Succeeded, recorded.Status);
        Assert.Contains("audit me", recorded.Input);
        Assert.Equal("echo", recorded.AgentName);
    }

    [Fact]
    public async Task A_long_result_is_cut_before_it_leaves()
    {
        var settings = Exposed("long_result");
        settings["Agent:McpServer:MaxResultChars"] = "100";
        var (app, client, _) = await StartAsync(settings);
        await using var _a = app;
        await using var _c = client;

        var result = await client.CallToolAsync("long_result", new Dictionary<string, object?>());

        var text = result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Single().Text;
        Assert.Contains("cut at 100", text);
        Assert.True(text.Length < 200);
    }

    [Theory]
    [InlineData("echo", "echo", true)]
    [InlineData("ECHO", "echo", true)]
    [InlineData("rag_*", "rag_search", true)]
    [InlineData("rag_*", "other", false)]
    [InlineData("echo", "echoes", false)]
    public void Names_match_exactly_or_by_trailing_wildcard(string pattern, string name, bool expected) =>
        Assert.Equal(expected, ExposedToolSet.Matches(pattern, name));

    [Fact]
    public void A_name_the_host_does_not_register_is_reported_and_does_not_break_the_rest()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(HostTools());

        var set = new ExposedToolSet(services.BuildServiceProvider(), new McpServerOptions { ExposedTools = ["echo", "ghost_*"] });

        Assert.Equal(["echo"], set.Tools.Select(t => t.Name));
        Assert.Equal(["ghost_*"], set.NotFound);
    }
}
