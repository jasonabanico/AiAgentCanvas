using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Capabilities.EventTriggers;
using AiAgentCanvas.Capabilities.RunLedger;
using AiAgentCanvas.Capabilities.Scheduling;
using AiAgentCanvas.Orchestration.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiAgentCanvas.Tests;

public class BudgetGuardTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"budget-{Guid.NewGuid():N}.db");
    private readonly SqliteRunLedger _ledger;
    private readonly ManualTime _time = new();
    private readonly RecordingSink _sink = new();

    public BudgetGuardTests() => _ledger = new SqliteRunLedger(_dbPath);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + suffix); }
            catch (IOException) { }
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingSink : INotificationSink
    {
        public List<AgentNotification> Sent { get; } = [];

        public Task SendAsync(AgentNotification notification, CancellationToken ct = default)
        {
            Sent.Add(notification);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<AgentNotification> SubscribeAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private BudgetGuard Guard(Action<BudgetOptions> configure, bool withSink = true)
    {
        var options = new BudgetOptions { Enabled = true, DeferMinutes = 30 };
        configure(options);
        return new BudgetGuard(_ledger, options, NullLogger<BudgetGuard>.Instance, withSink ? _sink : null, _time);
    }

    private void Spend(string agent, double cost, string? trigger = null, TimeSpan? ago = null, string? parent = null)
    {
        var id = Guid.NewGuid().ToString("N");
        var at = _time.Now - (ago ?? TimeSpan.FromMinutes(5));
        _ledger.Start(new RunRecord
        {
            Id = id, ParentId = parent, AgentName = agent, TriggerId = trigger,
            Source = RunSource.Trigger, StartedAt = at, Status = RunStatus.Running,
        });
        _ledger.Finish(id, RunStatus.Succeeded, "ok", null, new RunUsage(1, 1, 1, cost, 0, [], null), at.AddSeconds(30));
    }

    [Fact]
    public async Task Everything_is_allowed_when_budgets_are_off()
    {
        var guard = new BudgetGuard(_ledger, new BudgetOptions { Enabled = false, DailyLimit = 0.01 },
            NullLogger<BudgetGuard>.Instance, _sink, _time);
        Spend("agent", 100);

        Assert.True((await guard.CheckAsync("agent", null)).Allowed);
    }

    [Fact]
    public async Task An_agent_under_its_limit_is_allowed_and_one_at_the_limit_is_refused()
    {
        var guard = Guard(o => o.DailyLimit = 5);
        Spend("agent", 3);
        Assert.True((await guard.CheckAsync("agent", null)).Allowed);

        Spend("agent", 2);
        var decision = await guard.CheckAsync("agent", null);

        Assert.False(decision.Allowed);
        Assert.Equal("agent", decision.Scope);
        Assert.Equal(5.0, decision.Spent, 6);
        Assert.Equal(5.0, decision.Limit, 6);
        Assert.Equal(TimeSpan.FromMinutes(30), decision.RetryAfter);
        Assert.Contains("5.00 of its 5.00", decision.Reason);
    }

    [Fact]
    public async Task A_named_agent_limit_overrides_the_default_and_other_agents_are_unaffected()
    {
        var guard = Guard(o =>
        {
            o.DailyLimit = 100;
            o.Agents["frugal"] = 1;
        });
        Spend("frugal", 1.5);
        Spend("other", 1.5);

        Assert.False((await guard.CheckAsync("frugal", null)).Allowed);
        Assert.True((await guard.CheckAsync("other", null)).Allowed);
    }

    [Fact]
    public async Task A_trigger_limit_blocks_only_that_trigger()
    {
        var guard = Guard(o => o.Triggers["t-noisy"] = 2);
        Spend("agent", 2.5, trigger: "t-noisy");

        var blocked = await guard.CheckAsync("agent", "t-noisy");
        Assert.False(blocked.Allowed);
        Assert.Equal("trigger", blocked.Scope);
        Assert.True((await guard.CheckAsync("agent", "t-quiet")).Allowed);
        Assert.True((await guard.CheckAsync("agent", null)).Allowed);
    }

    [Fact]
    public async Task The_total_limit_blocks_every_agent()
    {
        var guard = Guard(o => o.TotalDailyLimit = 10);
        Spend("a", 6);
        Spend("b", 5);

        var decision = await guard.CheckAsync("c", null);

        Assert.False(decision.Allowed);
        Assert.Equal("total", decision.Scope);
    }

    [Fact]
    public async Task Spend_older_than_a_day_no_longer_counts()
    {
        var guard = Guard(o => o.DailyLimit = 5);
        Spend("agent", 10, ago: TimeSpan.FromHours(30));

        Assert.True((await guard.CheckAsync("agent", null)).Allowed);
    }

    [Fact]
    public async Task Delegated_work_is_not_counted_twice()
    {
        var guard = Guard(o => o.DailyLimit = 5);
        var parent = Guid.NewGuid().ToString("N");
        _ledger.Start(new RunRecord { Id = parent, AgentName = "agent", Source = RunSource.Trigger, StartedAt = _time.Now.AddMinutes(-5), Status = RunStatus.Running });
        _ledger.Finish(parent, RunStatus.Succeeded, "ok", null, new RunUsage(1, 1, 2, 3, 0, [], null), _time.Now);
        Spend("agent", 3, parent: parent);

        Assert.True((await guard.CheckAsync("agent", null)).Allowed);
    }

    [Fact]
    public async Task A_warning_is_sent_once_when_a_limit_is_nearly_spent()
    {
        var guard = Guard(o => o.DailyLimit = 10);
        Spend("agent", 8.5);

        Assert.True((await guard.CheckAsync("agent", null)).Allowed);
        Assert.True((await guard.CheckAsync("agent", null)).Allowed);

        var note = Assert.Single(_sink.Sent);
        Assert.Contains("nearing", note.Title);
        Assert.Equal("budget:agent:agent", note.Source);
    }

    [Fact]
    public async Task A_refusal_is_announced_once_per_day_and_again_the_next_day()
    {
        var guard = Guard(o => o.DailyLimit = 1);
        Spend("agent", 2);

        await guard.CheckAsync("agent", null);
        await guard.CheckAsync("agent", null);
        Assert.Single(_sink.Sent);

        _time.Now = _time.Now.AddDays(1);
        Spend("agent", 2);
        await guard.CheckAsync("agent", null);

        Assert.Equal(2, _sink.Sent.Count);
    }

    [Fact]
    public async Task Without_a_notification_sink_the_guard_still_decides()
    {
        var guard = Guard(o => o.DailyLimit = 1, withSink: false);
        Spend("agent", 2);

        Assert.False((await guard.CheckAsync("agent", null)).Allowed);
    }

    [Fact]
    public void Enabling_budgets_without_model_prices_fails_at_startup()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agent:Budgets:Enabled"] = "true",
        }).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAiAgentCanvasBudgets(config));

        Assert.Contains("no model rates", ex.Message);
    }

    [Fact]
    public void Enabling_budgets_with_model_prices_is_accepted()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agent:Budgets:Enabled"] = "true",
            ["Agent:Budgets:DailyLimit"] = "5",
            ["Agent:Pricing:Models:gpt-4o:InputPer1M"] = "2.5",
            ["Agent:Pricing:Models:gpt-4o:OutputPer1M"] = "10",
        }).Build();

        var services = new ServiceCollection();
        services.AddAiAgentCanvasBudgets(config);

        var options = services.BuildServiceProvider().GetRequiredService<BudgetOptions>();
        Assert.True(options.Enabled);
        Assert.Equal(5, options.DailyLimit);
    }

    [Fact]
    public async Task The_loop_guard_stops_a_run_that_has_spent_its_per_run_ceiling()
    {
        var options = new LoopGuardOptions { MaxToolRounds = 50, MaxRunCost = 1.0 };
        var inner = new FakeChatClient();
        var client = new LoopGuardChatClient(inner, options, new TiktokenCounter("gpt-4o"));

        var messages = new List<ChatMessage> { new(ChatRole.User, "go") };
        messages.Add(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("c0", "lookup", new Dictionary<string, object?> { ["q"] = 1 })]));
        messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c0", "r0")]));

        var chatOptions = new ChatOptions { Tools = [AIFunctionFactory.Create(() => "x", "lookup")] };

        // No tracked run on this flow, so the ceiling cannot be read and nothing happens.
        await client.GetResponseAsync(messages, chatOptions);
        Assert.NotNull(chatOptions.Tools);

        await RunTracking.RunAsync(_ledger, new RunStart(RunSource.Job, "agent"), async ct =>
        {
            AgentRunContext.Current!.AddModelUsage(10, 10, 0.4);
            await client.GetResponseAsync(messages, chatOptions, ct);
            Assert.NotNull(chatOptions.Tools);

            AgentRunContext.Current.AddModelUsage(10, 10, 0.7);
            await client.GetResponseAsync(messages, chatOptions, ct);
            return "ok";
        });

        Assert.Null(chatOptions.Tools);
        Assert.Contains("spend limit", inner.LastMessages[^1].Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("CostBudget", _ledger.Recent(new RunQuery()).Single().Termination);
    }
}

public class BudgetEnforcementTests : TriggerTestBase
{
    private sealed class BlockingGuard(bool allow) : IBudgetGuard
    {
        public int Checks;

        public Task<BudgetDecision> CheckAsync(string agentName, string? triggerId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Checks);
            return Task.FromResult(allow
                ? BudgetDecision.Allow
                : new BudgetDecision(false, "agent", 5, 5, "daily limit reached", TimeSpan.FromHours(2)));
        }
    }

    private sealed class CountingHandoff : IAgentHandoff
    {
        public int Calls;

        public Task<HandoffResult> HandoffAsync(string targetAgent, string context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HandoffResult { Status = "completed", Agent = targetAgent, Response = "done" });
        }
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 15_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("the condition was not met in time");
            await Task.Delay(25);
        }
    }

    private (TriggerDispatchService Service, TriggerEventQueue Queue, CountingHandoff Handoff, BlockingGuard Guard) Build(bool allow)
    {
        var options = Options();
        var queue = NewQueue(NewStore(), options);
        var handoff = new CountingHandoff();
        var guard = new BlockingGuard(allow);
        var services = new ServiceCollection()
            .AddSingleton<IAgentHandoff>(handoff)
            .AddSingleton<IBudgetGuard>(guard)
            .BuildServiceProvider();

        return (new TriggerDispatchService(queue, services, options, NullLogger<TriggerDispatchService>.Instance), queue, handoff, guard);
    }

    private static TriggerEvent ForAgent() =>
        new() { TriggerId = "t1", TriggerName = "t1", Message = "work", TargetAgent = "worker" };

    [Fact]
    public async Task A_refused_event_is_deferred_without_running_or_spending_an_attempt()
    {
        var (service, queue, handoff, guard) = Build(allow: false);
        queue.Enqueue(ForAgent());

        await service.StartAsync(CancellationToken.None);
        await WaitUntil(() => guard.Checks >= 1 && queue.Counts().Running == 0);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, handoff.Calls);
        Assert.Equal(new TriggerQueueCounts(1, 0, 0), queue.Counts());

        // Held for two hours, and the refusal cost it no attempt.
        Assert.Null(queue.ClaimNext(DateTimeOffset.UtcNow.AddHours(1)));
        var later = queue.ClaimNext(DateTimeOffset.UtcNow.AddHours(3))!;
        Assert.Equal(1, later.Attempts);
        Assert.Contains("daily limit reached", later.LastError);
    }

    [Fact]
    public async Task An_allowed_event_runs_normally()
    {
        var (service, queue, handoff, guard) = Build(allow: true);
        queue.Enqueue(ForAgent());

        await service.StartAsync(CancellationToken.None);
        await WaitUntil(() => handoff.Calls == 1);
        await service.StopAsync(CancellationToken.None);

        Assert.True(guard.Checks >= 1);
    }

    private sealed class MemoryTaskStore : IScheduledTaskStore
    {
        private readonly List<ScheduledTaskRecord> _tasks = [];
        public List<string> Marked { get; } = [];

        public void Add(ScheduledTaskRecord task) => _tasks.Add(task);
        public void SaveTask(ScheduledTaskRecord task) => _tasks.Add(task);
        public List<ScheduledTaskRecord> ListTasks() => _tasks.ToList();
        public bool RemoveTask(string id) => _tasks.RemoveAll(t => t.Id == id) > 0;
        public void SaveResult(string taskId, string description, string result) { }
        public List<ScheduledTaskResult> GetResults(int limit = 10) => [];

        public void MarkRun(string id, DateTimeOffset runAt, string? error = null)
        {
            Marked.Add(id);
            var task = _tasks.Single(t => t.Id == id);
            task.LastRunAt = runAt;
            task.LastError = error;
        }

        public void Dispose() { }
    }

    private static async Task<MemoryTaskStore> RunSchedulerOnce(IBudgetGuard guard)
    {
        var store = new MemoryTaskStore();
        store.Add(new ScheduledTaskRecord { Id = "task-1", Description = "daily", Prompt = "do it", IsRecurring = false });

        var services = new ServiceCollection().BuildServiceProvider();
        var job = new ScheduledAgentJob(services, store, NullLogger<ScheduledAgentJob>.Instance);
        var runner = new ScheduledTaskRunner(store, job, new SchedulerOptions { TickSeconds = 3600 },
            NullLogger<ScheduledTaskRunner>.Instance, guard);

        await runner.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await runner.StopAsync(CancellationToken.None);
        return store;
    }

    [Fact]
    public async Task A_scheduled_task_refused_by_a_limit_is_left_unclaimed_so_it_runs_later()
    {
        var store = await RunSchedulerOnce(new BlockingGuard(allow: false));

        Assert.Empty(store.Marked);
        Assert.Null(store.ListTasks().Single().LastRunAt);
    }

    [Fact]
    public async Task A_scheduled_task_allowed_by_the_guard_is_claimed_and_attempted()
    {
        // No agent is registered in this test, so the attempt itself fails. Being
        // claimed and marked with an error proves the guard let it through.
        var store = await RunSchedulerOnce(new BlockingGuard(allow: true));

        Assert.Contains("task-1", store.Marked);
        Assert.NotNull(store.ListTasks().Single().LastError);
    }
}
