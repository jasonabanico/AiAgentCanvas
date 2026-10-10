#pragma warning disable MEAI001, MAAI001

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AiAgentCanvas.Capabilities.AgentOrchestration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AiAgentCanvas.Tests;

public class OrchestrationToolTests : OrchestrationTestBase
{
    private IReadOnlyList<AITool> Tools(out OrchestrationRunner runner)
    {
        var options = Options();
        runner = Runner(options);
        return OrchestrationToolProvider.CreateTools(runner, new OrchestrationStore(options.DatabasePath));
    }

    private static async Task<JsonElement> CallAsync(IReadOnlyList<AITool> tools, string name, Dictionary<string, object?> args)
    {
        var tool = tools.OfType<AIFunction>().Single(t => t.Name == name);
        var result = await tool.InvokeAsync(new AIFunctionArguments(args));
        return JsonDocument.Parse(result is JsonElement e ? e.GetString()! : result!.ToString()!).RootElement;
    }

    [Fact]
    public void An_agent_has_no_tool_to_answer_a_run_that_is_waiting_for_a_person()
    {
        var tools = Tools(out _);

        Assert.Equal(["cancel_orchestration", "get_orchestration", "list_orchestrations", "start_orchestration"],
            tools.Select(t => t.Name).OrderBy(n => n));
        Assert.DoesNotContain(tools, t => t.Name.Contains("respond", StringComparison.OrdinalIgnoreCase)
                                          || t.Name.Contains("approve", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Starting_a_group_chat_through_the_tool_returns_the_transcript()
    {
        AddAgent("a", "one");
        AddAgent("b", "two");
        var tools = Tools(out _);

        var result = await CallAsync(tools, "start_orchestration",
            new() { ["kind"] = "groupchat", ["task"] = "Talk.", ["agents"] = new[] { "a", "b" }, ["maxRounds"] = 2 });

        Assert.Equal("Completed", result.GetProperty("status").GetString());
        Assert.Equal(2, result.GetProperty("transcript").GetArrayLength());
    }

    [Fact]
    public async Task A_run_waiting_for_a_person_tells_the_agent_not_to_answer_it()
    {
        AddAgent("manager", "Facts.", "Plan: do it.");
        AddAgent("worker", "x");
        var tools = Tools(out _);

        var result = await CallAsync(tools, "start_orchestration",
            new() { ["kind"] = "Magentic", ["task"] = "Do it.", ["agents"] = new[] { "worker" }, ["lead"] = "manager" });

        Assert.Equal("WaitingForInput", result.GetProperty("status").GetString());
        Assert.Contains("person must answer", result.GetProperty("note").GetString());
        Assert.Equal("plan_review", result.GetProperty("pending").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Bad_requests_come_back_as_errors_not_exceptions()
    {
        AddAgent("a", "one");
        var tools = Tools(out _);

        var badKind = await CallAsync(tools, "start_orchestration", new() { ["kind"] = "Swarm", ["task"] = "t", ["agents"] = new[] { "a" } });
        var oneAgent = await CallAsync(tools, "start_orchestration", new() { ["kind"] = "GroupChat", ["task"] = "t", ["agents"] = new[] { "a" } });
        var missing = await CallAsync(tools, "get_orchestration", new() { ["runId"] = "nope" });

        Assert.Contains("kind must be", badKind.GetProperty("error").GetString());
        Assert.Contains("two", oneAgent.GetProperty("error").GetString());
        Assert.Contains("No orchestration run", missing.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Runs_can_be_listed_by_status_and_a_waiting_run_cancelled()
    {
        AddAgent("manager", "Facts.", "Plan.");
        AddAgent("worker", "x");
        AddAgent("a", "one");
        AddAgent("b", "two");
        var tools = Tools(out _);
        var waiting = await CallAsync(tools, "start_orchestration",
            new() { ["kind"] = "Magentic", ["task"] = "Do it.", ["agents"] = new[] { "worker" }, ["lead"] = "manager" });
        await CallAsync(tools, "start_orchestration", new() { ["kind"] = "GroupChat", ["task"] = "Talk.", ["agents"] = new[] { "a", "b" }, ["maxRounds"] = 2 });

        var listed = await CallAsync(tools, "list_orchestrations", new() { ["status"] = "WaitingForInput" });
        var cancelled = await CallAsync(tools, "cancel_orchestration", new() { ["runId"] = waiting.GetProperty("id").GetString() });

        Assert.Single(listed.GetProperty("runs").EnumerateArray());
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
    }
}

public class OrchestrationEndpointTests : OrchestrationTestBase
{
    private async Task<(WebApplication App, HttpClient Client)> StartAsync()
    {
        var options = Options();
        var store = new OrchestrationStore(options.DatabasePath);
        var runner = Runner(options);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(runner);
        var app = builder.Build();
        app.MapOrchestrationEndpoints();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (app, new HttpClient { BaseAddress = new Uri(address) });
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private void Team()
    {
        AddAgent("manager", "Facts.", "Plan: worker does it.", "{\"is_request_satisfied\":{\"answer\":true,\"reason\":\"r\"},\"is_in_loop\":{\"answer\":false,\"reason\":\"r\"},\"is_progress_being_made\":{\"answer\":true,\"reason\":\"r\"},\"next_speaker\":{\"answer\":\"worker\",\"reason\":\"r\"},\"instruction_or_question\":{\"answer\":\"go\",\"reason\":\"r\"}}", "All done.");
        AddAgent("worker", "Worked.");
    }

    [Fact]
    public async Task A_person_starts_a_run_reads_it_and_approves_it_over_http()
    {
        Team();
        var (app, client) = await StartAsync();
        await using var _ = app;

        var started = await client.PostAsJsonAsync("/api/orchestrations",
            new { kind = "Magentic", task = "Do it.", agents = new[] { "worker" }, lead = "manager" });
        var run = await JsonOf(started);
        var id = run.GetProperty("id").GetString();

        Assert.Equal("WaitingForInput", run.GetProperty("status").GetString());

        var fetched = await JsonOf(await client.GetAsync($"/api/orchestrations/{id}"));
        Assert.Equal("plan_review", fetched.GetProperty("pending").GetProperty("kind").GetString());

        var answered = await JsonOf(await client.PostAsJsonAsync($"/api/orchestrations/{id}/respond", new { approve = true }));
        Assert.Equal("Completed", answered.GetProperty("status").GetString());
        Assert.Equal("All done.", answered.GetProperty("result").GetString());
    }

    [Fact]
    public async Task Asking_for_changes_without_feedback_is_a_bad_request()
    {
        Team();
        var (app, client) = await StartAsync();
        await using var _ = app;
        var run = await JsonOf(await client.PostAsJsonAsync("/api/orchestrations",
            new { kind = "Magentic", task = "Do it.", agents = new[] { "worker" }, lead = "manager" }));

        var response = await client.PostAsJsonAsync($"/api/orchestrations/{run.GetProperty("id").GetString()}/respond", new { approve = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_runs_and_bad_kinds_are_reported_with_the_right_status()
    {
        Team();
        var (app, client) = await StartAsync();
        await using var _ = app;

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/orchestrations/nope")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/orchestrations/nope/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/orchestrations",
            new { kind = "Swarm", task = "t", agents = new[] { "worker" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/orchestrations/nope/respond", new { approve = true })).StatusCode);
    }

    [Fact]
    public async Task The_list_filters_by_status_and_reports_enums_by_name()
    {
        Team();
        var (app, client) = await StartAsync();
        await using var _ = app;
        await client.PostAsJsonAsync("/api/orchestrations", new { kind = "Magentic", task = "Do it.", agents = new[] { "worker" }, lead = "manager" });

        var waiting = await client.GetStringAsync("/api/orchestrations?status=WaitingForInput");
        var completed = await client.GetStringAsync("/api/orchestrations?status=Completed");

        Assert.Contains("\"status\":\"WaitingForInput\"", waiting);
        Assert.Contains("\"kind\":\"Magentic\"", waiting);
        Assert.Equal("[]", completed);
    }
}
