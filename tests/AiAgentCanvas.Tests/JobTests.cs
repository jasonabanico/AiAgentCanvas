using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Capabilities.EventTriggers;
using AiAgentCanvas.Capabilities.Jobs;
using AiAgentCanvas.Capabilities.RunLedger;
using AiAgentCanvas.Capabilities.Scheduling;
using DataConnection.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiAgentCanvas.Tests;

public sealed class LambdaJob(string name, Func<JobContext, CancellationToken, Task<JobResult>> run, string description = "a test job") : IAgentJob
{
    public string Name => name;
    public string Description => description;
    public Task<JobResult> RunAsync(JobContext context, CancellationToken ct) => run(context, ct);
}

public class JobRunnerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"jobs-{Guid.NewGuid():N}.db");
    private readonly SqliteRunLedger _ledger;

    public JobRunnerTests() => _ledger = new SqliteRunLedger(_dbPath);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + suffix); }
            catch (IOException) { }
        }
    }

    private JobRunner Runner(IEnumerable<IAgentJob> jobs, Action<JobOptions>? configure = null)
    {
        var options = new JobOptions();
        configure?.Invoke(options);
        return new JobRunner(jobs, new ServiceCollection().BuildServiceProvider(), options, NullLogger<JobRunner>.Instance, _ledger);
    }

    private static LambdaJob Ok(string name = "echo") =>
        new(name, (ctx, _) => Task.FromResult(JobResult.Success($"hello {ctx.Argument("who") ?? "nobody"}")));

    [Fact]
    public async Task A_job_runs_with_its_arguments_and_is_recorded_in_the_ledger()
    {
        var runner = Runner([Ok()]);

        var result = await runner.RunAsync(new JobRequest("echo", new Dictionary<string, string> { ["who"] = "world" }, TriggerId: "t1", TaskId: "task-1"));

        Assert.True(result.Ok);
        Assert.Equal("hello world", result.Summary);

        var run = _ledger.Recent(new RunQuery()).Single();
        Assert.Equal(RunSource.Job, run.Source);
        Assert.Equal("echo", run.AgentName);
        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Equal("hello world", run.Output);
        Assert.Equal("who=world", run.Input);
        Assert.Equal("t1", run.TriggerId);
        Assert.Equal("task-1", run.TaskId);
    }

    [Fact]
    public async Task A_job_that_reports_a_problem_is_a_failed_run_with_its_own_message()
    {
        var runner = Runner([new LambdaJob("check", (_, _) => Task.FromResult(JobResult.Failure("3 invoices overdue")))]);

        var result = await runner.RunAsync(new JobRequest("check"));

        Assert.False(result.Ok);
        Assert.Equal("3 invoices overdue", result.Summary);
        var run = _ledger.Recent(new RunQuery()).Single();
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("3 invoices overdue", run.Error);
    }

    [Fact]
    public async Task A_job_that_throws_is_reported_as_a_failure_and_does_not_escape()
    {
        var runner = Runner([new LambdaJob("broken", (_, _) => throw new InvalidOperationException("the api is down"))]);

        var result = await runner.RunAsync(new JobRequest("broken"));

        Assert.False(result.Ok);
        Assert.Equal("the api is down", result.Summary);
        Assert.Equal(RunStatus.Failed, _ledger.Recent(new RunQuery()).Single().Status);
    }

    [Fact]
    public async Task An_unknown_job_names_the_ones_that_exist()
    {
        var runner = Runner([Ok("alpha"), Ok("beta")]);

        var result = await runner.RunAsync(new JobRequest("gamma"));

        Assert.False(result.Ok);
        Assert.Contains("alpha, beta", result.Summary);
        Assert.Empty(_ledger.Recent(new RunQuery()));
    }

    [Fact]
    public async Task A_job_that_runs_too_long_is_stopped_and_recorded_as_failed()
    {
        var runner = Runner(
            [new LambdaJob("slow", async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return JobResult.Success("never");
            })],
            o => o.TimeoutSeconds = 1);

        var result = await runner.RunAsync(new JobRequest("slow"));

        Assert.False(result.Ok);
        Assert.Contains("timed out", result.Summary);
        Assert.Equal(RunStatus.Failed, _ledger.Recent(new RunQuery()).Single().Status);
    }

    [Fact]
    public async Task A_job_does_not_overlap_with_itself()
    {
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var runner = Runner([new LambdaJob("long", async (_, _) =>
        {
            started.SetResult();
            await release.Task;
            return JobResult.Success("finished");
        })]);

        var first = runner.RunAsync(new JobRequest("long"));
        await started.Task;

        var second = await runner.RunAsync(new JobRequest("long"));
        Assert.True(second.Ok);
        Assert.Contains("Skipped", second.Summary);

        release.SetResult();
        Assert.Equal("finished", (await first).Summary);
        Assert.Single(_ledger.Recent(new RunQuery()));
    }

    [Fact]
    public async Task Different_jobs_can_run_at_the_same_time()
    {
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var runner = Runner([
            new LambdaJob("one", async (_, _) =>
            {
                started.SetResult();
                await release.Task;
                return JobResult.Success("one done");
            }),
            Ok("two"),
        ]);

        var first = runner.RunAsync(new JobRequest("one"));
        await started.Task;

        Assert.Equal("hello nobody", (await runner.RunAsync(new JobRequest("two"))).Summary);

        release.SetResult();
        await first;
    }

    [Fact]
    public void Bad_or_duplicate_names_are_rejected_when_the_runner_is_built()
    {
        Assert.Throws<InvalidOperationException>(() => Runner([Ok("Has Spaces")]));
        Assert.Throws<InvalidOperationException>(() => Runner([Ok("UPPER")]));
        var ex = Assert.Throws<InvalidOperationException>(() => Runner([Ok("same"), Ok("same")]));
        Assert.Contains("same", ex.Message);
    }

    [Fact]
    public async Task Cancelling_the_caller_cancels_the_run()
    {
        using var cts = new CancellationTokenSource();
        var runner = Runner([new LambdaJob("wait", async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return JobResult.Success("never");
        })]);

        var running = runner.RunAsync(new JobRequest("wait"), cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task The_list_and_run_tools_work_for_an_agent()
    {
        var runner = Runner([Ok("echo")]);
        var tools = JobToolProvider.CreateTools(runner).Cast<AIFunction>().ToDictionary(t => t.Name);

        var listed = (await tools["list_jobs"].InvokeAsync(new AIFunctionArguments()))!.ToString()!;
        Assert.Contains("echo", listed);

        var ran = (await tools["run_job"].InvokeAsync(new AIFunctionArguments
        {
            ["name"] = "echo",
            ["arguments"] = new Dictionary<string, string> { ["who"] = "agent" },
        }))!.ToString()!;
        Assert.Contains("hello agent", ran);
    }
}

public class JobIntegrationTests : TriggerTestBase
{
    private sealed class FakeJobRunner(Func<JobRequest, JobResult> respond) : IJobRunner
    {
        public List<JobRequest> Requests { get; } = [];

        public IReadOnlyList<JobInfo> List() => [new("report", "a report")];

        public Task<JobResult> RunAsync(JobRequest request, CancellationToken ct = default)
        {
            lock (Requests) Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class MemoryTaskStore : IScheduledTaskStore
    {
        private readonly List<ScheduledTaskRecord> _tasks = [];
        public List<(string Id, string Result)> Results { get; } = [];

        public void SaveTask(ScheduledTaskRecord task) => _tasks.Add(task);
        public List<ScheduledTaskRecord> ListTasks() => _tasks.ToList();
        public bool RemoveTask(string id) => _tasks.RemoveAll(t => t.Id == id) > 0;
        public void SaveResult(string taskId, string description, string result) => Results.Add((taskId, result));
        public List<ScheduledTaskResult> GetResults(int limit = 10) => [];

        public void MarkRun(string id, DateTimeOffset runAt, string? error = null)
        {
            var task = _tasks.Single(t => t.Id == id);
            task.LastRunAt = runAt;
            task.LastError = error;
        }

        public void Dispose() { }
    }

    private sealed class BlockAgentTasks : IBudgetGuard
    {
        public Task<BudgetDecision> CheckAsync(string agentName, string? triggerId, CancellationToken ct = default) =>
            Task.FromResult(new BudgetDecision(false, "agent", 1, 1, "limit reached", TimeSpan.FromHours(1)));
    }

    private static async Task<MemoryTaskStore> RunScheduler(ScheduledTaskRecord task, IJobRunner? jobs, IBudgetGuard? budget = null)
    {
        var store = new MemoryTaskStore();
        store.SaveTask(task);

        var services = new ServiceCollection().BuildServiceProvider();
        var job = new ScheduledAgentJob(services, store, NullLogger<ScheduledAgentJob>.Instance);
        var runner = new ScheduledTaskRunner(store, job, new SchedulerOptions { TickSeconds = 3600 },
            NullLogger<ScheduledTaskRunner>.Instance, budget, jobs);

        await runner.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await runner.StopAsync(CancellationToken.None);
        return store;
    }

    private static ScheduledTaskRecord JobTask() => new()
    {
        Id = "task-job",
        Description = "weekly report",
        JobName = "report",
        JobArguments = new Dictionary<string, string> { ["week"] = "42" },
    };

    [Fact]
    public async Task A_scheduled_job_task_runs_the_job_with_its_arguments_and_removes_a_one_shot()
    {
        var jobs = new FakeJobRunner(_ => JobResult.Success("report sent"));

        var store = await RunScheduler(JobTask(), jobs);

        var request = Assert.Single(jobs.Requests);
        Assert.Equal("report", request.Name);
        Assert.Equal("42", request.Arguments!["week"]);
        Assert.Equal("task-job", request.TaskId);
        Assert.Equal(("task-job", "report sent"), Assert.Single(store.Results));
        Assert.Empty(store.ListTasks());
    }

    [Fact]
    public async Task A_failing_scheduled_job_is_marked_with_its_error()
    {
        var store = await RunScheduler(JobTask(), new FakeJobRunner(_ => JobResult.Failure("api down")));

        var task = store.ListTasks().Single();
        Assert.Equal("api down", task.LastError);
    }

    [Fact]
    public async Task A_job_task_without_the_jobs_capability_says_so()
    {
        var store = await RunScheduler(JobTask(), jobs: null);

        Assert.Contains("Jobs capability is not enabled", store.ListTasks().Single().LastError);
    }

    [Fact]
    public async Task A_spend_limit_on_the_agent_does_not_hold_back_a_job()
    {
        var jobs = new FakeJobRunner(_ => JobResult.Success("done"));

        await RunScheduler(JobTask(), jobs, new BlockAgentTasks());

        Assert.Single(jobs.Requests);
    }

    private (TriggerDispatchService Service, TriggerEventQueue Queue) BuildDispatcher(IJobRunner? jobs, Action<EventTriggerOptions>? configure = null)
    {
        var options = Options(o =>
        {
            o.RetryBaseSeconds = 0;
            configure?.Invoke(o);
        });
        var queue = NewQueue(NewStore(), options);
        var services = new ServiceCollection();
        if (jobs is not null)
            services.AddSingleton(jobs);

        return (new TriggerDispatchService(queue, services.BuildServiceProvider(), options, NullLogger<TriggerDispatchService>.Instance), queue);
    }

    private static TriggerEvent JobEvent() => new()
    {
        TriggerId = "t-job",
        TriggerName = "inbound",
        Message = "order 42 arrived",
        TargetJob = "report",
        Metadata = { ["source"] = "webhook" },
    };

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 15_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("the condition was not met in time");
            await Task.Delay(25);
        }
    }

    [Fact]
    public async Task A_trigger_that_names_a_job_runs_it_with_the_event_as_arguments()
    {
        var jobs = new FakeJobRunner(_ => JobResult.Success("handled"));
        var (service, queue) = BuildDispatcher(jobs);
        queue.Enqueue(JobEvent());

        await service.StartAsync(CancellationToken.None);
        await WaitUntil(() => jobs.Requests.Count == 1 && queue.Counts() == new TriggerQueueCounts(0, 0, 0));
        await service.StopAsync(CancellationToken.None);

        var request = jobs.Requests.Single();
        Assert.Equal("report", request.Name);
        Assert.Equal("t-job", request.TriggerId);
        Assert.Equal("order 42 arrived", request.Arguments!["message"]);
        Assert.Equal("t-job", request.Arguments["triggerId"]);
        Assert.Equal("webhook", request.Arguments["source"]);
    }

    [Fact]
    public async Task A_failing_job_event_is_retried_then_parked()
    {
        var jobs = new FakeJobRunner(_ => JobResult.Failure("still broken"));
        var (service, queue) = BuildDispatcher(jobs, o => o.MaxAttempts = 2);
        queue.Enqueue(JobEvent());

        await service.StartAsync(CancellationToken.None);
        await WaitUntil(() => queue.Counts().Dead == 1);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(2, jobs.Requests.Count);
        Assert.Contains("still broken", queue.Dead().Single().LastError);
    }

    [Fact]
    public async Task A_job_trigger_without_the_jobs_capability_is_parked_with_a_clear_reason()
    {
        var (service, queue) = BuildDispatcher(jobs: null, o => o.MaxAttempts = 1);
        queue.Enqueue(JobEvent());

        await service.StartAsync(CancellationToken.None);
        await WaitUntil(() => queue.Counts().Dead == 1);
        await service.StopAsync(CancellationToken.None);

        Assert.Contains("Jobs capability is not enabled", queue.Dead().Single().LastError);
    }

    [Fact]
    public void A_trigger_and_its_events_keep_their_target_job_across_a_restart()
    {
        var registry = new TriggerRegistry(NullLogger<TriggerRegistry>.Instance, NewStore());
        var trigger = new EventTrigger { Name = "t", Type = EventTriggerType.Webhook, AgentMessage = "x", TargetJob = "report" };
        registry.Register(trigger);
        NewQueue(NewStore()).Enqueue(JobEvent());

        Assert.Equal("report", new TriggerRegistry(NullLogger<TriggerRegistry>.Instance, NewStore()).Get(trigger.Id)!.TargetJob);
        Assert.Equal("report", NewQueue(NewStore()).ClaimNext(DateTimeOffset.UtcNow)!.TargetJob);
    }

    [Fact]
    public void A_scheduled_job_task_survives_in_the_sqlite_store_with_its_arguments()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sched-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteScheduledTaskStore($"Data Source={path}");
            store.SaveTask(new ScheduledTaskRecord
            {
                Id = "task-1",
                Description = "daily",
                JobName = "report",
                JobArguments = new Dictionary<string, string> { ["a"] = "1" },
                CronExpression = "0 8 * * *",
                IsRecurring = true,
            });
            store.SaveTask(new ScheduledTaskRecord { Id = "task-2", Description = "agent task", Prompt = "do it" });

            var tasks = new SqliteScheduledTaskStore($"Data Source={path}").ListTasks().ToDictionary(t => t.Id);

            Assert.Equal("report", tasks["task-1"].JobName);
            Assert.Equal("1", tasks["task-1"].JobArguments!["a"]);
            Assert.Null(tasks["task-2"].JobName);
            Assert.Equal("do it", tasks["task-2"].Prompt);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); }
            catch (IOException) { }
        }
    }

    [Fact]
    public async Task The_schedule_job_tool_validates_the_job_and_the_cron_expression()
    {
        var store = new MemoryTaskStore();
        var provider = new SchedulerToolProvider(store, new FakeJobRunner(_ => JobResult.Success("ok")));
        var schedule = provider.GetTools().Cast<AIFunction>().Single(t => t.Name == "schedule_job");

        var unknown = (await schedule.InvokeAsync(new AIFunctionArguments { ["description"] = "d", ["jobName"] = "nope" }))!.ToString()!;
        Assert.Contains("No job named", unknown);
        Assert.Contains("nope", unknown);

        var badCron = (await schedule.InvokeAsync(new AIFunctionArguments { ["description"] = "d", ["jobName"] = "report", ["cronExpression"] = "not cron" }))!.ToString()!;
        Assert.Contains("not a valid", badCron);

        var good = (await schedule.InvokeAsync(new AIFunctionArguments
        {
            ["description"] = "weekly",
            ["jobName"] = "report",
            ["cronExpression"] = "0 8 * * 1",
            ["arguments"] = new Dictionary<string, string> { ["week"] = "now" },
        }))!.ToString()!;
        Assert.Contains("scheduled", good);

        var saved = store.ListTasks().Single();
        Assert.Equal("report", saved.JobName);
        Assert.True(saved.IsRecurring);
        Assert.Equal("now", saved.JobArguments!["week"]);
    }

    [Fact]
    public void The_schedule_job_tool_is_not_offered_without_the_jobs_capability()
    {
        var names = new SchedulerToolProvider(new MemoryTaskStore()).GetTools().Select(t => t.Name).ToList();

        Assert.DoesNotContain("schedule_job", names);
        Assert.Contains("schedule_task", names);
    }
}

public class BuiltInJobTests : TriggerTestBase
{
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

    private static (JobContext Context, RecordingSink Sink) Context(Dictionary<string, string>? args = null)
    {
        var sink = new RecordingSink();
        var services = new ServiceCollection().AddSingleton<INotificationSink>(sink).BuildServiceProvider();
        return (new JobContext(services, args ?? []), sink);
    }

    [Fact]
    public async Task The_queue_health_job_is_quiet_when_nothing_is_parked()
    {
        var queue = NewQueue(NewStore());
        queue.Enqueue(Event());
        var (context, sink) = Context();

        var result = await new TriggerQueueHealthJob(queue).RunAsync(context, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Contains("healthy", result.Summary);
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public async Task The_queue_health_job_fails_and_notifies_when_events_are_parked()
    {
        var queue = NewQueue(NewStore(), Options(o =>
        {
            o.MaxAttempts = 1;
            o.RetryBaseSeconds = 0;
        }));
        queue.Enqueue(Event());
        queue.Fail(queue.ClaimNext(DateTimeOffset.UtcNow)!, "api returned 500", DateTimeOffset.UtcNow);
        var (context, sink) = Context();

        var result = await new TriggerQueueHealthJob(queue).RunAsync(context, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("api returned 500", result.Summary);
        Assert.Equal("job:trigger-queue-health", Assert.Single(sink.Sent).Source);
    }

    [Fact]
    public async Task The_failure_report_job_summarizes_failures_and_notifies()
    {
        var path = Path.Combine(Path.GetTempPath(), $"report-{Guid.NewGuid():N}.db");
        try
        {
            var ledger = new SqliteRunLedger(path);
            void Run(string id, string agent, RunStatus status)
            {
                ledger.Start(new RunRecord { Id = id, AgentName = agent, Source = RunSource.Trigger, StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10), Status = RunStatus.Running });
                ledger.Finish(id, status, null, status == RunStatus.Failed ? "tool exploded" : null, new RunUsage(1, 1, 1, 0.5, 0, [], null), DateTimeOffset.UtcNow);
            }
            Run("a", "briefer", RunStatus.Succeeded);
            Run("b", "billing", RunStatus.Failed);
            Run("c", "billing", RunStatus.Failed);

            var (context, sink) = Context();
            var result = await new RunFailureReportJob(ledger).RunAsync(context, CancellationToken.None);

            Assert.True(result.Ok);
            Assert.Contains("2 failed", result.Summary);
            Assert.Contains("billing x2", result.Summary);
            Assert.Contains("tool exploded", result.Summary);
            Assert.Equal("2", result.Data!["failed"]);
            Assert.Single(sink.Sent);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(path + suffix); }
                catch (IOException) { }
            }
        }
    }

    [Fact]
    public async Task The_failure_report_job_stays_quiet_when_everything_succeeded()
    {
        var path = Path.Combine(Path.GetTempPath(), $"report-ok-{Guid.NewGuid():N}.db");
        try
        {
            var ledger = new SqliteRunLedger(path);
            var (context, sink) = Context(new() { ["hours"] = "6" });

            var result = await new RunFailureReportJob(ledger).RunAsync(context, CancellationToken.None);

            Assert.True(result.Ok);
            Assert.Contains("No failed runs in the last 6h", result.Summary);
            Assert.Empty(sink.Sent);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(path + suffix); }
                catch (IOException) { }
            }
        }
    }
}
