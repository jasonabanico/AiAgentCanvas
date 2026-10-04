using System.Net;
using System.Text;
using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Capabilities.EventTriggers;
using AiAgentCanvas.Capabilities.RunLedger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiAgentCanvas.Tests;

public abstract class TriggerTestBase : IDisposable
{
    protected readonly string DbPath = Path.Combine(Path.GetTempPath(), $"triggers-{Guid.NewGuid():N}.db");

    protected static EventTriggerOptions Options(Action<EventTriggerOptions>? configure = null)
    {
        var options = new EventTriggerOptions
        {
            QueueCapacity = 16,
            MaxAttempts = 3,
            RetryBaseSeconds = 30,
            IdlePollSeconds = 1,
        };
        configure?.Invoke(options);
        return options;
    }

    protected TriggerStore NewStore() => new(DbPath);

    protected static TriggerEventQueue NewQueue(TriggerStore store, EventTriggerOptions? options = null) =>
        new(store, options ?? Options(), NullLogger<TriggerEventQueue>.Instance);

    protected static TriggerEvent Event(string trigger = "t1", string? key = null, string message = "go") => new()
    {
        TriggerId = trigger,
        TriggerName = $"trigger {trigger}",
        Message = message,
        DedupeKey = key,
    };

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(DbPath + suffix); }
            catch (IOException) { }
        }
    }
}

public class TriggerQueueTests : TriggerTestBase
{
    [Fact]
    public void A_new_event_is_accepted_and_counted_as_pending()
    {
        var queue = NewQueue(NewStore());

        Assert.Equal(EnqueueOutcome.Accepted, queue.Enqueue(Event()));
        Assert.Equal(new TriggerQueueCounts(1, 0, 0), queue.Counts());
    }

    [Fact]
    public void The_same_occurrence_is_a_duplicate_but_a_different_one_is_not()
    {
        var queue = NewQueue(NewStore());

        Assert.Equal(EnqueueOutcome.Accepted, queue.Enqueue(Event(key: "delivery-1")));
        Assert.Equal(EnqueueOutcome.Duplicate, queue.Enqueue(Event(key: "delivery-1")));
        Assert.Equal(EnqueueOutcome.Accepted, queue.Enqueue(Event(key: "delivery-2")));
        Assert.Equal(EnqueueOutcome.Accepted, queue.Enqueue(Event(trigger: "t2", key: "delivery-1")));
        Assert.Equal(3, queue.Counts().Pending);
    }

    [Fact]
    public void Events_without_a_key_are_never_duplicates()
    {
        var queue = NewQueue(NewStore());

        Assert.Equal(EnqueueOutcome.Accepted, queue.Enqueue(Event()));
        Assert.Equal(EnqueueOutcome.Accepted, queue.Enqueue(Event()));
    }

    [Fact]
    public void Events_are_refused_not_dropped_when_the_backlog_is_full()
    {
        var queue = NewQueue(NewStore(), Options(o => o.QueueCapacity = 16));

        for (var i = 0; i < 16; i++)
            Assert.Equal(EnqueueOutcome.Accepted, queue.Enqueue(Event(key: $"k{i}")));

        Assert.Equal(EnqueueOutcome.Rejected, queue.Enqueue(Event(key: "one-too-many")));
        Assert.Equal(16, queue.Counts().Pending);
    }

    [Fact]
    public void Claiming_takes_the_oldest_event_and_marks_it_running()
    {
        var queue = NewQueue(NewStore());
        queue.Enqueue(Event(message: "first"));
        queue.Enqueue(Event(message: "second"));

        var claimed = queue.ClaimNext(DateTimeOffset.UtcNow);

        Assert.Equal("first", claimed!.Message);
        Assert.Equal(1, claimed.Attempts);
        Assert.Equal(new TriggerQueueCounts(1, 1, 0), queue.Counts());
        Assert.Null(NewQueue(NewStore()).ClaimNext(DateTimeOffset.UtcNow.AddYears(-1)));
    }

    [Fact]
    public void An_empty_queue_has_nothing_to_claim()
    {
        Assert.Null(NewQueue(NewStore()).ClaimNext(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void A_failed_event_waits_out_its_backoff_before_it_can_be_claimed_again()
    {
        var queue = NewQueue(NewStore(), Options(o => o.RetryBaseSeconds = 30));
        queue.Enqueue(Event());
        var now = DateTimeOffset.UtcNow;

        var first = queue.ClaimNext(now)!;
        Assert.False(queue.Fail(first, "boom", now));

        Assert.Null(queue.ClaimNext(now.AddSeconds(5)));
        Assert.NotNull(queue.ClaimNext(now.AddSeconds(31)));
    }

    [Fact]
    public void Backoff_doubles_and_is_capped()
    {
        var queue = NewQueue(NewStore(), Options(o =>
        {
            o.RetryBaseSeconds = 30;
            o.MaxRetryDelayMinutes = 2;
        }));

        Assert.Equal(TimeSpan.FromSeconds(30), queue.RetryDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(60), queue.RetryDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(120), queue.RetryDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(120), queue.RetryDelay(10));
    }

    [Fact]
    public void An_event_that_keeps_failing_is_parked_as_dead_after_its_last_attempt()
    {
        var queue = NewQueue(NewStore(), Options(o =>
        {
            o.MaxAttempts = 2;
            o.RetryBaseSeconds = 0;
        }));
        queue.Enqueue(Event());

        var first = queue.ClaimNext(DateTimeOffset.UtcNow)!;
        Assert.False(queue.Fail(first, "first failure", DateTimeOffset.UtcNow));

        var second = queue.ClaimNext(DateTimeOffset.UtcNow.AddSeconds(1))!;
        Assert.Equal(2, second.Attempts);
        Assert.True(queue.Fail(second, "second failure", DateTimeOffset.UtcNow));

        Assert.Equal(new TriggerQueueCounts(0, 0, 1), queue.Counts());
        var dead = queue.Dead().Single();
        Assert.Equal("second failure", dead.LastError);
        Assert.Null(queue.ClaimNext(DateTimeOffset.UtcNow.AddDays(1)));
    }

    [Fact]
    public void A_dead_event_can_be_requeued_with_a_fresh_set_of_attempts()
    {
        var queue = NewQueue(NewStore(), Options(o =>
        {
            o.MaxAttempts = 1;
            o.RetryBaseSeconds = 0;
        }));
        queue.Enqueue(Event());
        var claimed = queue.ClaimNext(DateTimeOffset.UtcNow)!;
        Assert.True(queue.Fail(claimed, "boom", DateTimeOffset.UtcNow));

        Assert.True(queue.Retry(claimed.Id));
        Assert.False(queue.Retry("no-such-event"));

        var again = queue.ClaimNext(DateTimeOffset.UtcNow.AddSeconds(1))!;
        Assert.Equal(1, again.Attempts);
    }

    [Fact]
    public void Events_left_running_by_a_stopped_process_are_queued_again()
    {
        var queue = NewQueue(NewStore());
        queue.Enqueue(Event());
        queue.ClaimNext(DateTimeOffset.UtcNow);

        var recovered = NewQueue(NewStore()).RecoverInterrupted();

        Assert.Equal(1, recovered);
        Assert.NotNull(NewQueue(NewStore()).ClaimNext(DateTimeOffset.UtcNow.AddSeconds(1)));
    }

    [Fact]
    public void Releasing_a_claimed_event_does_not_spend_an_attempt()
    {
        var queue = NewQueue(NewStore());
        queue.Enqueue(Event());
        var claimed = queue.ClaimNext(DateTimeOffset.UtcNow)!;

        queue.Release(claimed);

        var again = queue.ClaimNext(DateTimeOffset.UtcNow.AddSeconds(1))!;
        Assert.Equal(1, again.Attempts);
    }

    [Fact]
    public void Queued_events_survive_a_restart()
    {
        NewQueue(NewStore()).Enqueue(Event(message: "survives"));

        var reopened = NewQueue(NewStore());

        Assert.Equal(1, reopened.Counts().Pending);
        Assert.Equal("survives", reopened.ClaimNext(DateTimeOffset.UtcNow)!.Message);
    }

    [Fact]
    public void Succeeded_events_are_pruned_but_dead_ones_are_kept()
    {
        var store = NewStore();
        var queue = NewQueue(store, Options(o =>
        {
            o.MaxAttempts = 1;
            o.RetryBaseSeconds = 0;
        }));
        queue.Enqueue(Event(key: "ok"));
        queue.Enqueue(Event(key: "bad"));
        queue.Complete(queue.ClaimNext(DateTimeOffset.UtcNow)!);
        queue.Fail(queue.ClaimNext(DateTimeOffset.UtcNow)!, "boom", DateTimeOffset.UtcNow);

        var removed = store.PruneSucceeded(DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Equal(1, removed);
        Assert.Equal(1, queue.Counts().Dead);
    }

    [Fact]
    public void Cursors_round_trip_and_update()
    {
        var store = NewStore();

        Assert.Null(store.Get("gmail:inbox"));
        store.Set("gmail:inbox", "history-100");
        store.Set("gmail:inbox", "history-250");

        Assert.Equal("history-250", NewStore().Get("gmail:inbox"));
    }

    [Fact]
    public void Triggers_survive_a_restart_with_their_changes()
    {
        var registry = new TriggerRegistry(NullLogger<TriggerRegistry>.Instance, NewStore());
        var trigger = new EventTrigger
        {
            Name = "morning",
            Type = EventTriggerType.Scheduled,
            CronExpression = "0 9 * * *",
            AgentMessage = "brief me",
            TargetAgent = "briefer",
        };
        registry.Register(trigger);
        registry.RecordFired(trigger.Id);
        registry.SetEnabled(trigger.Id, false);

        var reloaded = new TriggerRegistry(NullLogger<TriggerRegistry>.Instance, NewStore()).Get(trigger.Id)!;

        Assert.Equal("morning", reloaded.Name);
        Assert.Equal("0 9 * * *", reloaded.CronExpression);
        Assert.Equal("briefer", reloaded.TargetAgent);
        Assert.False(reloaded.Enabled);
        Assert.Equal(1, reloaded.FireCount);
        Assert.NotNull(reloaded.LastFired);

        new TriggerRegistry(NullLogger<TriggerRegistry>.Instance, NewStore()).Remove(trigger.Id);
        Assert.Null(new TriggerRegistry(NullLogger<TriggerRegistry>.Instance, NewStore()).Get(trigger.Id));
    }

    [Fact]
    public void The_registry_still_works_in_memory_without_a_store()
    {
        var registry = new TriggerRegistry(NullLogger<TriggerRegistry>.Instance);
        var trigger = new EventTrigger { Name = "t", Type = EventTriggerType.Webhook, AgentMessage = "x" };

        registry.Register(trigger);

        Assert.Same(trigger, registry.Get(trigger.Id));
        Assert.False(registry.SetEnabled("missing", true));
    }
}

public class TriggerDispatchTests : TriggerTestBase
{
    private sealed class FakeHandoff(Func<string, HandoffResult> respond) : IAgentHandoff
    {
        public int Calls;

        public Task<HandoffResult> HandoffAsync(string targetAgent, string context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(respond(context));
        }
    }

    private static HandoffResult Ok(string text = "done") => new() { Status = "completed", Agent = "worker", Response = text };
    private static HandoffResult Failed() => new() { Status = "failed", Agent = "worker", Error = "agent exploded" };

    private (TriggerDispatchService Service, TriggerEventQueue Queue, FakeHandoff Handoff, SqliteRunLedger Ledger) Build(
        Func<string, HandoffResult> respond, Action<EventTriggerOptions>? configure = null)
    {
        var options = Options(o =>
        {
            o.RetryBaseSeconds = 0;
            configure?.Invoke(o);
        });
        var queue = NewQueue(NewStore(), options);
        var handoff = new FakeHandoff(respond);
        var ledger = new SqliteRunLedger(Path.Combine(Path.GetTempPath(), $"dispatch-runs-{Guid.NewGuid():N}.db"));

        var services = new ServiceCollection()
            .AddSingleton<IAgentHandoff>(handoff)
            .AddSingleton<IRunLedger>(ledger)
            .BuildServiceProvider();

        return (new TriggerDispatchService(queue, services, options, NullLogger<TriggerDispatchService>.Instance), queue, handoff, ledger);
    }

    private static TriggerEvent ForAgent(string key) =>
        new() { TriggerId = "t1", TriggerName = "t1", Message = "do the thing", TargetAgent = "worker", DedupeKey = key };

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 15_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("the condition was not met in time");
            await Task.Delay(25);
        }
    }

    [Fact]
    public async Task A_queued_event_is_run_marked_succeeded_and_recorded_in_the_ledger()
    {
        var (service, queue, handoff, ledger) = Build(_ => Ok("all good"));
        queue.Enqueue(ForAgent("a"));

        await service.StartAsync(CancellationToken.None);
        await WaitUntil(() => queue.Counts() == new TriggerQueueCounts(0, 0, 0) && handoff.Calls == 1);
        await service.StopAsync(CancellationToken.None);

        var run = ledger.Recent(new RunQuery(Source: RunSource.Trigger)).Single();
        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Equal("t1", run.TriggerId);
        Assert.Equal("all good", run.Output);
    }

    [Fact]
    public async Task A_failing_event_is_retried_then_parked_as_dead()
    {
        var (service, queue, handoff, ledger) = Build(_ => Failed(), o => o.MaxAttempts = 3);
        queue.Enqueue(ForAgent("b"));

        await service.StartAsync(CancellationToken.None);
        await WaitUntil(() => queue.Counts().Dead == 1);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(3, handoff.Calls);
        Assert.Contains("agent exploded", queue.Dead().Single().LastError);
        Assert.Equal(3, ledger.Recent(new RunQuery(Status: RunStatus.Failed, Source: RunSource.Trigger)).Count);
    }

    [Fact]
    public async Task An_event_left_running_by_a_previous_process_is_picked_up_on_start()
    {
        var (service, queue, handoff, _) = Build(_ => Ok());
        queue.Enqueue(ForAgent("c"));
        queue.ClaimNext(DateTimeOffset.UtcNow);
        Assert.Equal(1, queue.Counts().Running);

        await service.StartAsync(CancellationToken.None);
        await WaitUntil(() => handoff.Calls == 1 && queue.Counts().Running == 0);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(new TriggerQueueCounts(0, 0, 0), queue.Counts());
    }

    [Fact]
    public async Task Events_added_while_the_dispatcher_is_idle_are_handled_promptly()
    {
        var (service, queue, handoff, _) = Build(_ => Ok());

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        queue.Enqueue(ForAgent("d"));
        await WaitUntil(() => handoff.Calls == 1);
        await service.StopAsync(CancellationToken.None);
    }
}

public class WebhookEndpointTests : TriggerTestBase
{
    private async Task<(WebApplication App, HttpClient Client, TriggerRegistry Registry, TriggerEventQueue Queue)> StartAsync(
        Action<EventTriggerOptions>? configure = null)
    {
        var options = Options(configure);
        var store = NewStore();
        var registry = new TriggerRegistry(NullLogger<TriggerRegistry>.Instance, store);
        var queue = NewQueue(store, options);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(registry);
        builder.Services.AddSingleton(queue);
        var app = builder.Build();
        app.MapEventTriggerEndpoints();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (app, new HttpClient { BaseAddress = new Uri(address) }, registry, queue);
    }

    private static EventTrigger Webhook(bool enabled = true) => new()
    {
        Name = "inbound",
        Type = EventTriggerType.Webhook,
        AgentMessage = "handle this",
        Enabled = enabled,
    };

    private static StringContent Body(string text) => new(text, Encoding.UTF8, "application/json");

    [Fact]
    public async Task A_webhook_is_accepted_and_queued_with_its_payload()
    {
        var (app, client, registry, queue) = await StartAsync();
        await using var _ = app;
        var trigger = Webhook();
        registry.Register(trigger);

        var response = await client.PostAsync($"/api/triggers/webhook/{trigger.Id}", Body("{\"order\":42}"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var claimed = queue.ClaimNext(DateTimeOffset.UtcNow)!;
        Assert.Contains("{\"order\":42}", claimed.Message);
        Assert.Equal(1, registry.Get(trigger.Id)!.FireCount);
    }

    [Fact]
    public async Task A_retry_with_the_same_idempotency_key_is_recognised_as_a_duplicate()
    {
        var (app, client, registry, queue) = await StartAsync();
        await using var _ = app;
        var trigger = Webhook();
        registry.Register(trigger);

        async Task<HttpStatusCode> Send()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/triggers/webhook/{trigger.Id}") { Content = Body("{}") };
            request.Headers.Add(WebhookEndpoints.IdempotencyHeader, "delivery-77");
            return (await client.SendAsync(request)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.Accepted, await Send());
        Assert.Equal(HttpStatusCode.OK, await Send());
        Assert.Equal(1, queue.Counts().Pending);
    }

    [Fact]
    public async Task An_unknown_or_disabled_trigger_is_not_found()
    {
        var (app, client, registry, _) = await StartAsync();
        await using var _ = app;
        var disabled = Webhook(enabled: false);
        registry.Register(disabled);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/triggers/webhook/nope", Body("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/triggers/webhook/{disabled.Id}", Body("{}"))).StatusCode);
    }

    [Fact]
    public async Task An_oversized_body_is_refused()
    {
        var (app, client, registry, queue) = await StartAsync(o => o.MaxWebhookBodyBytes = 2048);
        await using var _ = app;
        var trigger = Webhook();
        registry.Register(trigger);

        var response = await client.PostAsync($"/api/triggers/webhook/{trigger.Id}", Body(new string('x', 5000)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, queue.Counts().Pending);
    }

    [Fact]
    public async Task A_full_backlog_answers_503_with_retry_after_instead_of_dropping_the_event()
    {
        var (app, client, registry, queue) = await StartAsync(o => o.QueueCapacity = 16);
        await using var _ = app;
        var trigger = Webhook();
        registry.Register(trigger);
        for (var i = 0; i < 16; i++)
            queue.Enqueue(Event(trigger.Id, key: $"fill-{i}"));

        var response = await client.PostAsync($"/api/triggers/webhook/{trigger.Id}", Body("{}"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.Contains("Retry-After"));
        Assert.Equal(0, registry.Get(trigger.Id)!.FireCount);
    }

    [Fact]
    public async Task Dead_events_can_be_listed_and_retried_over_http()
    {
        var (app, client, _, queue) = await StartAsync(o =>
        {
            o.MaxAttempts = 1;
            o.RetryBaseSeconds = 0;
        });
        await using var _ = app;
        queue.Enqueue(Event());
        var claimed = queue.ClaimNext(DateTimeOffset.UtcNow)!;
        queue.Fail(claimed, "boom", DateTimeOffset.UtcNow);

        var list = await client.GetStringAsync("/api/triggers/events/dead");
        Assert.Contains(claimed.Id, list);

        var retry = await client.PostAsync($"/api/triggers/events/{claimed.Id}/retry", null);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(1, queue.Counts().Pending);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/triggers/events/nope/retry", null)).StatusCode);
    }
}
