using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Capabilities.RunLedger;
using AiAgentCanvas.Orchestration.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AiAgentCanvas.Tests;

public class AgentRunContextTests
{
    private static AgentRunContext NewRun(AgentRunContext? parent = null) =>
        new(Guid.NewGuid().ToString("N"), RunSource.Scheduled, "agent", parent);

    [Fact]
    public void Enter_sets_the_ambient_run_and_restores_the_previous_one()
    {
        Assert.Null(AgentRunContext.Current);

        var outer = NewRun();
        using (outer.Enter())
        {
            Assert.Same(outer, AgentRunContext.Current);

            var inner = NewRun(outer);
            using (inner.Enter())
                Assert.Same(inner, AgentRunContext.Current);

            Assert.Same(outer, AgentRunContext.Current);
        }

        Assert.Null(AgentRunContext.Current);
    }

    [Fact]
    public async Task The_ambient_run_survives_awaits_and_parallel_work()
    {
        var run = NewRun();
        using var scope = run.Enter();

        async Task<AgentRunContext?> ReadAfterDelay()
        {
            await Task.Delay(5);
            return AgentRunContext.Current;
        }

        var seen = await Task.WhenAll(ReadAfterDelay(), ReadAfterDelay(), Task.Run(() => AgentRunContext.Current));

        Assert.All(seen, s => Assert.Same(run, s));
    }

    [Fact]
    public void Usage_accumulates_on_the_run_and_rolls_up_to_its_parent()
    {
        var parent = NewRun();
        var child = NewRun(parent);

        child.AddModelUsage(100, 50, 0.01);
        child.AddModelUsage(200, 25, 0.02);
        child.AddToolCall("lookup", "ok", 12);

        var childUsage = child.Snapshot();
        Assert.Equal(300, childUsage.InputTokens);
        Assert.Equal(75, childUsage.OutputTokens);
        Assert.Equal(2, childUsage.ModelCalls);
        Assert.Equal(0.03, childUsage.EstimatedCost, 9);
        Assert.Single(childUsage.ToolCalls);

        var parentUsage = parent.Snapshot();
        Assert.Equal(300, parentUsage.InputTokens);
        Assert.Equal(2, parentUsage.ModelCalls);
        Assert.Equal(0.03, parentUsage.EstimatedCost, 9);
        Assert.Equal(1, parentUsage.ToolCallCount);
        Assert.Empty(parentUsage.ToolCalls);
    }

    [Fact]
    public void Tool_call_detail_is_capped_but_the_count_keeps_going()
    {
        var run = NewRun();
        for (var i = 0; i < AgentRunContext.MaxRecordedToolCalls + 50; i++)
            run.AddToolCall("lookup", "ok", 1);

        var usage = run.Snapshot();
        Assert.Equal(AgentRunContext.MaxRecordedToolCalls + 50, usage.ToolCallCount);
        Assert.Equal(AgentRunContext.MaxRecordedToolCalls, usage.ToolCalls.Count);
    }

    [Fact]
    public void The_first_termination_reason_is_kept()
    {
        var run = NewRun();
        run.SetTermination("MaxToolRounds");
        run.SetTermination("TokenBudget");

        Assert.Equal("MaxToolRounds", run.Snapshot().Termination);
    }
}

public class RunLedgerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"runs-{Guid.NewGuid():N}.db");
    private readonly SqliteRunLedger _ledger;

    public RunLedgerTests() => _ledger = new SqliteRunLedger(_dbPath, maxTextChars: 300);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + suffix); }
            catch (IOException) { }
        }
    }

    private static RunUsage Usage(double cost = 0, int tools = 0, string? termination = null) =>
        new(10, 5, 1, cost, tools, Enumerable.Range(0, tools).Select(i => new ToolCallRecord($"tool{i}", "ok", 1)).ToList(), termination);

    private RunRecord Start(string id, string agent = "agent", RunSource source = RunSource.Scheduled,
        string? parent = null, string? trigger = null, DateTimeOffset? at = null)
    {
        var run = new RunRecord
        {
            Id = id,
            ParentId = parent,
            AgentName = agent,
            Source = source,
            TriggerId = trigger,
            StartedAt = at ?? DateTimeOffset.UtcNow,
            Status = RunStatus.Running,
            Input = $"input of {id}",
        };
        _ledger.Start(run);
        return run;
    }

    [Fact]
    public void A_finished_run_round_trips_with_usage_and_tool_calls()
    {
        Start("r1", trigger: "t1");
        _ledger.Finish("r1", RunStatus.Succeeded, "done", null, Usage(cost: 0.25, tools: 2, termination: "MaxToolRounds"), DateTimeOffset.UtcNow);

        var run = _ledger.Get("r1")!;
        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Equal("done", run.Output);
        Assert.Equal("input of r1", run.Input);
        Assert.Equal("t1", run.TriggerId);
        Assert.Equal(0.25, run.EstimatedCost, 9);
        Assert.Equal(2, run.ToolCallCount);
        Assert.Equal(2, run.ToolCalls.Count);
        Assert.Equal("MaxToolRounds", run.Termination);
        Assert.NotNull(run.EndedAt);
    }

    [Fact]
    public void Long_text_is_truncated()
    {
        var run = Start("r1");
        _ledger.Finish("r1", RunStatus.Succeeded, new string('x', 5000), null, Usage(), DateTimeOffset.UtcNow);

        var stored = _ledger.Get("r1")!;
        Assert.EndsWith("...[truncated]", stored.Output);
        Assert.True(stored.Output!.Length < 400);
    }

    [Fact]
    public void Recent_filters_by_agent_status_source_and_trigger()
    {
        Start("a", agent: "alpha", source: RunSource.Scheduled);
        Start("b", agent: "beta", source: RunSource.Trigger, trigger: "t9");
        _ledger.Finish("a", RunStatus.Succeeded, "ok", null, Usage(), DateTimeOffset.UtcNow);
        _ledger.Finish("b", RunStatus.Failed, null, "boom", Usage(), DateTimeOffset.UtcNow);

        Assert.Single(_ledger.Recent(new RunQuery(AgentName: "alpha")));
        Assert.Equal("b", _ledger.Recent(new RunQuery(Status: RunStatus.Failed)).Single().Id);
        Assert.Equal("b", _ledger.Recent(new RunQuery(Source: RunSource.Trigger)).Single().Id);
        Assert.Equal("b", _ledger.Recent(new RunQuery(TriggerId: "t9")).Single().Id);
        Assert.Equal(2, _ledger.Recent(new RunQuery()).Count);
    }

    [Fact]
    public void Recent_orders_newest_first_and_honours_the_limit()
    {
        Start("old", at: DateTimeOffset.UtcNow.AddHours(-3));
        Start("mid", at: DateTimeOffset.UtcNow.AddHours(-2));
        Start("new", at: DateTimeOffset.UtcNow.AddHours(-1));

        var runs = _ledger.Recent(new RunQuery(Limit: 2));

        Assert.Equal(["new", "mid"], runs.Select(r => r.Id));
    }

    [Fact]
    public void Totals_count_top_level_runs_only()
    {
        Start("parent");
        Start("child", parent: "parent");
        _ledger.Finish("parent", RunStatus.Succeeded, "ok", null, new RunUsage(100, 50, 2, 0.5, 0, [], null), DateTimeOffset.UtcNow);
        _ledger.Finish("child", RunStatus.Succeeded, "ok", null, new RunUsage(60, 30, 1, 0.3, 0, [], null), DateTimeOffset.UtcNow);

        var totals = _ledger.Totals(DateTimeOffset.UtcNow.AddHours(-1));

        Assert.Equal(1, totals.Runs);
        Assert.Equal(100, totals.InputTokens);
        Assert.Equal(0.5, totals.EstimatedCost, 9);
        Assert.Single(_ledger.Children("parent"));
    }

    [Fact]
    public void Prune_removes_only_finished_runs_older_than_the_cutoff()
    {
        Start("old-done", at: DateTimeOffset.UtcNow.AddDays(-40));
        _ledger.Finish("old-done", RunStatus.Succeeded, "ok", null, Usage(), DateTimeOffset.UtcNow.AddDays(-40));
        Start("old-running", at: DateTimeOffset.UtcNow.AddDays(-40));
        Start("recent");
        _ledger.Finish("recent", RunStatus.Succeeded, "ok", null, Usage(), DateTimeOffset.UtcNow);

        var removed = _ledger.Prune(DateTimeOffset.UtcNow.AddDays(-30));

        Assert.Equal(1, removed);
        Assert.Null(_ledger.Get("old-done"));
        Assert.NotNull(_ledger.Get("old-running"));
        Assert.NotNull(_ledger.Get("recent"));
    }

    [Fact]
    public void Runs_left_in_flight_are_marked_abandoned()
    {
        Start("stuck");
        Start("fine");
        _ledger.Finish("fine", RunStatus.Succeeded, "ok", null, Usage(), DateTimeOffset.UtcNow);

        Assert.Equal(1, _ledger.MarkAbandoned());

        var stuck = _ledger.Get("stuck")!;
        Assert.Equal(RunStatus.Abandoned, stuck.Status);
        Assert.NotNull(stuck.EndedAt);
        Assert.Equal(RunStatus.Succeeded, _ledger.Get("fine")!.Status);
    }

    [Fact]
    public async Task Tracking_records_a_successful_run_with_the_usage_written_during_it()
    {
        var output = await RunTracking.RunAsync(
            _ledger,
            new RunStart(RunSource.Scheduled, "agent", "do it", TaskId: "task-1"),
            _ =>
            {
                AgentRunContext.Current!.AddModelUsage(120, 30, 0.04);
                AgentRunContext.Current.AddToolCall("lookup", "ok", 8);
                return Task.FromResult("finished");
            });

        Assert.Equal("finished", output);

        var run = _ledger.Recent(new RunQuery()).Single();
        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Equal("finished", run.Output);
        Assert.Equal("task-1", run.TaskId);
        Assert.Equal(120, run.InputTokens);
        Assert.Equal(1, run.ToolCallCount);
        Assert.Equal(0.04, run.EstimatedCost, 9);
    }

    [Fact]
    public async Task Tracking_records_a_failure_and_rethrows()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RunTracking.RunAsync(
            _ledger,
            new RunStart(RunSource.Trigger, "agent", "x", TriggerId: "t1"),
            _ => throw new InvalidOperationException("tool exploded")));

        Assert.Equal("tool exploded", ex.Message);

        var run = _ledger.Recent(new RunQuery()).Single();
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("tool exploded", run.Error);
    }

    [Fact]
    public async Task Tracking_records_cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunTracking.RunAsync(
            _ledger,
            new RunStart(RunSource.Job, "job"),
            async ct =>
            {
                await Task.Delay(1000, ct);
                return "never";
            },
            cts.Token));

        Assert.Equal(RunStatus.Cancelled, _ledger.Recent(new RunQuery()).Single().Status);
    }

    [Fact]
    public async Task A_nested_run_is_a_child_and_its_usage_rolls_up()
    {
        await RunTracking.RunAsync(
            _ledger,
            new RunStart(RunSource.Trigger, "outer", "go"),
            async ct =>
            {
                AgentRunContext.Current!.AddModelUsage(10, 5, 0.01);

                await RunTracking.RunAsync(
                    _ledger,
                    new RunStart(RunSource.Handoff, "inner", "delegated"),
                    _ =>
                    {
                        AgentRunContext.Current!.AddModelUsage(40, 20, 0.04);
                        return Task.FromResult("inner done");
                    },
                    ct);

                return "outer done";
            });

        var top = _ledger.Recent(new RunQuery(TopLevelOnly: true)).Single();
        Assert.Equal("outer", top.AgentName);
        Assert.Equal(50, top.InputTokens);
        Assert.Equal(0.05, top.EstimatedCost, 9);

        var child = _ledger.Children(top.Id).Single();
        Assert.Equal("inner", child.AgentName);
        Assert.Equal(40, child.InputTokens);
        Assert.Equal(top.Id, child.ParentId);
    }

    [Fact]
    public async Task A_ledger_failure_does_not_fail_the_run()
    {
        var errors = new List<Exception>();

        var output = await RunTracking.RunAsync(
            new ThrowingLedger(),
            new RunStart(RunSource.Scheduled, "agent"),
            _ => Task.FromResult("still ran"),
            onLedgerError: errors.Add);

        Assert.Equal("still ran", output);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public async Task Tracking_works_without_a_ledger()
    {
        AgentRunContext? seen = null;

        var output = await RunTracking.RunAsync(
            null,
            new RunStart(RunSource.Scheduled, "agent"),
            _ =>
            {
                seen = AgentRunContext.Current;
                return Task.FromResult("ok");
            });

        Assert.Equal("ok", output);
        Assert.NotNull(seen);
        Assert.Null(AgentRunContext.Current);
    }

    [Fact]
    public async Task The_cost_client_writes_tokens_and_spend_to_the_ambient_run()
    {
        var pricing = new ModelPricingOptions
        {
            Models = { ["test-model"] = new ModelRate { InputPer1M = 2, OutputPer1M = 10 } },
        };
        var client = new CostTrackingChatClient(
            new UsageChatClient("test-model", new UsageDetails { InputTokenCount = 1_000_000, OutputTokenCount = 100_000 }),
            pricing);

        await RunTracking.RunAsync(_ledger, new RunStart(RunSource.Job, "agent"), async ct =>
        {
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: ct);
            return "ok";
        });

        var run = _ledger.Recent(new RunQuery()).Single();
        Assert.Equal(1, run.ModelCalls);
        Assert.Equal(1_000_000, run.InputTokens);
        Assert.Equal(100_000, run.OutputTokens);
        Assert.Equal(3.0, run.EstimatedCost, 9);
    }

    [Fact]
    public async Task The_traced_tool_wrapper_records_each_call_on_the_ambient_run()
    {
        var tool = new TracedAIFunction(AIFunctionFactory.Create(() => "pong", "ping"));

        await RunTracking.RunAsync(_ledger, new RunStart(RunSource.Job, "agent"), async _ =>
        {
            await tool.InvokeAsync(new AIFunctionArguments());
            await tool.InvokeAsync(new AIFunctionArguments());
            return "ok";
        });

        var run = _ledger.Recent(new RunQuery()).Single();
        Assert.Equal(2, run.ToolCallCount);
        Assert.All(run.ToolCalls, c => Assert.Equal("ping", c.Name));
    }

    [Fact]
    public async Task The_loop_guard_records_why_it_cut_a_run_short()
    {
        var options = new LoopGuardOptions { MaxToolRounds = 2 };
        var client = new LoopGuardChatClient(new FakeChatClient(), options, new TiktokenCounter("gpt-4o"));

        var messages = new List<ChatMessage> { new(ChatRole.User, "find it") };
        for (var i = 0; i < 2; i++)
        {
            messages.Add(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent($"c{i}", "lookup", new Dictionary<string, object?> { ["q"] = i })]));
            messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent($"c{i}", $"r{i}")]));
        }

        await RunTracking.RunAsync(_ledger, new RunStart(RunSource.Job, "agent"), async ct =>
        {
            await client.GetResponseAsync(messages, new ChatOptions { Tools = [AIFunctionFactory.Create(() => "x", "lookup")] }, ct);
            return "ok";
        });

        Assert.Equal("MaxToolRounds", _ledger.Recent(new RunQuery()).Single().Termination);
    }

    private sealed class ThrowingLedger : IRunLedger
    {
        public void Start(RunRecord run) => throw new IOException("disk full");
        public void Finish(string runId, RunStatus status, string? output, string? error, RunUsage usage, DateTimeOffset endedAt) => throw new IOException("disk full");
        public IReadOnlyList<RunRecord> Recent(RunQuery query) => [];
        public RunRecord? Get(string runId) => null;
        public IReadOnlyList<RunRecord> Children(string parentId) => [];
        public RunTotals Totals(DateTimeOffset since, string? agentName = null, string? triggerId = null) => new(0, 0, 0, 0, 0);
        public int Prune(DateTimeOffset olderThan) => 0;
        public int MarkAbandoned() => 0;
    }

    private sealed class UsageChatClient(string modelId, UsageDetails usage) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")) { ModelId = modelId, Usage = usage });

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}

public class RunLedgerEndpointTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"runs-api-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + suffix); }
            catch (IOException) { }
        }
    }

    [Fact]
    public async Task The_endpoints_report_status_and_source_by_name_in_camel_case()
    {
        var ledger = new SqliteRunLedger(_dbPath, maxTextChars: 300);
        ledger.Start(new RunRecord
        {
            Id = "r1", AgentName = "agent", Source = RunSource.Trigger, StartedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Running,
        });
        ledger.Finish("r1", RunStatus.Failed, null, "boom",
            new RunUsage(10, 5, 1, 0.5, 0, [], null), DateTimeOffset.UtcNow);

        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IRunLedger>(ledger);
        await using var app = builder.Build();
        app.MapRunLedgerEndpoints();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.First();
        using var client = new HttpClient { BaseAddress = new Uri(address) };

        var list = await client.GetStringAsync("/api/runs");
        var one = await client.GetStringAsync("/api/runs/r1");
        var totals = await client.GetStringAsync("/api/runs/totals");

        Assert.Contains("\"status\":\"Failed\"", list);
        Assert.Contains("\"source\":\"Trigger\"", list);
        Assert.Contains("\"agentName\":\"agent\"", list);
        Assert.Contains("\"children\":[]", one);
        Assert.Contains("\"estimatedCost\":0.5", totals);
    }
}
