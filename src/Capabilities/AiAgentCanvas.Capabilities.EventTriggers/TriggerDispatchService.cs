using System.Diagnostics;
using AiAgentCanvas.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.EventTriggers;

public sealed class EventTriggerOptions
{
    public const string SectionName = "Agent:EventTriggers";

    /// <summary>Where trigger definitions, queued events and cursors are stored.</summary>
    public string DatabasePath { get; set; } = "event-triggers.db";

    /// <summary>How often the evaluation loop checks scheduled triggers.</summary>
    public int PollSeconds { get; set; } = 30;

    /// <summary>
    /// Events waiting or running before new ones are refused. A cap is the point: an
    /// unbounded queue turns a trigger storm into a disk and spend problem. A refused
    /// event is reported to the sender, not dropped silently.
    /// </summary>
    public int QueueCapacity { get; set; } = 256;

    /// <summary>Wall-clock ceiling on the agent run a single event starts.</summary>
    public int RunTimeoutMinutes { get; set; } = 10;

    /// <summary>Attempts before an event is parked as dead for an operator to inspect.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Delay before the second attempt. Each later attempt doubles it.</summary>
    public int RetryBaseSeconds { get; set; } = 30;

    public int MaxRetryDelayMinutes { get; set; } = 15;

    /// <summary>How long the dispatcher sleeps when the queue is empty or nothing is due.</summary>
    public int IdlePollSeconds { get; set; } = 5;

    /// <summary>Succeeded events are deleted after this many days. Zero keeps them.</summary>
    public int SucceededRetentionDays { get; set; } = 7;

    /// <summary>Webhook bodies larger than this are refused.</summary>
    public int MaxWebhookBodyBytes { get; set; } = 262_144;
}

/// <summary>
/// Drains the durable event queue and runs the agent for each event. A failed run is
/// retried with backoff and, once its attempts are spent, parked as dead. Without this
/// consumer, triggers would fire into a queue nothing reads.
/// </summary>
public sealed class TriggerDispatchService : BackgroundService
{
    private readonly TriggerEventQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly EventTriggerOptions _options;
    private readonly ILogger<TriggerDispatchService> _logger;

    public TriggerDispatchService(
        TriggerEventQueue queue,
        IServiceProvider serviceProvider,
        EventTriggerOptions options,
        ILogger<TriggerDispatchService> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var recovered = _queue.RecoverInterrupted();
        if (recovered > 0)
            _logger.LogWarning("{Count} trigger event(s) were running when the process stopped and are queued again", recovered);

        _logger.LogInformation("Trigger dispatcher started");

        var nextPrune = DateTimeOffset.UtcNow;
        var idle = TimeSpan.FromSeconds(Math.Max(1, _options.IdlePollSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow >= nextPrune)
                {
                    _queue.PruneSucceeded();
                    nextPrune = DateTimeOffset.UtcNow.AddHours(1);
                }

                var evt = _queue.ClaimNext(DateTimeOffset.UtcNow);
                if (evt is null)
                {
                    await _queue.WaitForWorkAsync(idle, stoppingToken);
                    continue;
                }

                await ProcessAsync(evt, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Trigger dispatch loop error");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }

    private async Task ProcessAsync(TriggerEvent evt, CancellationToken stoppingToken)
    {
        try
        {
            var guard = _serviceProvider.GetService<IBudgetGuard>();
            if (guard is not null && string.IsNullOrWhiteSpace(evt.TargetJob))
            {
                var decision = await guard.CheckAsync(AgentNameFor(evt), evt.TriggerId, stoppingToken);
                if (!decision.Allowed)
                {
                    // Refused, not failed: the event keeps its attempts and waits for the
                    // limit to clear instead of being retried into the same wall.
                    _queue.Defer(evt, DateTimeOffset.UtcNow + (decision.RetryAfter ?? TimeSpan.FromHours(1)),
                        decision.Reason ?? "spend limit reached");
                    return;
                }
            }

            await DispatchAsync(evt, stoppingToken);
            _queue.Complete(evt);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // A clean shutdown is not a failed attempt.
            _queue.Release(evt);
            throw;
        }
        catch (Exception ex)
        {
            var died = _queue.Fail(evt, ex.Message, DateTimeOffset.UtcNow);
            if (died)
                await NotifyDeadAsync(evt, ex.Message, stoppingToken);
        }
    }

    private async Task DispatchAsync(TriggerEvent evt, CancellationToken stoppingToken)
    {
        using var activity = AgentTelemetry.Source.StartActivity("trigger dispatch", ActivityKind.Internal);
        activity?.SetTag("trigger.id", evt.TriggerId);
        activity?.SetTag("trigger.name", evt.TriggerName);
        activity?.SetTag("trigger.target_agent", evt.TargetAgent ?? "default");
        activity?.SetTag("trigger.attempt", evt.Attempts);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, _options.RunTimeoutMinutes)));

        var result = await RunAsync(evt, timeout.Token);

        _logger.LogInformation("Trigger {TriggerName} produced {Length} characters of output",
            evt.TriggerName, result.Length);

        var sink = _serviceProvider.GetService<INotificationSink>();
        if (sink is not null)
        {
            await sink.SendAsync(new AgentNotification
            {
                Title = $"Trigger fired: {evt.TriggerName}",
                Body = result.Length > 500 ? result[..500] + "..." : result,
                Source = $"trigger:{evt.TriggerId}",
            }, stoppingToken);
        }
    }

    private async Task NotifyDeadAsync(TriggerEvent evt, string error, CancellationToken ct)
    {
        var sink = _serviceProvider.GetService<INotificationSink>();
        if (sink is null)
            return;

        try
        {
            await sink.SendAsync(new AgentNotification
            {
                Title = $"Trigger event failed: {evt.TriggerName}",
                Body = $"The event {evt.Id} failed {evt.Attempts} attempts and was parked. Last error: {error}",
                Source = $"trigger:{evt.TriggerId}",
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send the dead-event notification for {EventId}", evt.Id);
        }
    }

    private string AgentNameFor(TriggerEvent evt) =>
        !string.IsNullOrWhiteSpace(evt.TargetAgent)
            ? evt.TargetAgent
            : _serviceProvider.GetService<AIAgent>()?.Name ?? "default";

    private Task<string> RunAsync(TriggerEvent evt, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(evt.TargetJob))
            return RunJobAsync(evt, ct);

        return RunTracking.RunAsync(
            _serviceProvider.GetService<IRunLedger>(),
            new RunStart(RunSource.Trigger, AgentNameFor(evt), evt.Message, TriggerId: evt.TriggerId),
            token => RunUntrackedAsync(evt, token),
            ct,
            ex => _logger.LogWarning(ex, "Run ledger write failed for trigger {TriggerId}", evt.TriggerId));
    }

    /// <summary>
    /// A trigger that names a job runs it with no model. The job runner records the run,
    /// so this does not wrap it in a second record.
    /// </summary>
    private async Task<string> RunJobAsync(TriggerEvent evt, CancellationToken ct)
    {
        var runner = _serviceProvider.GetService<IJobRunner>()
            ?? throw new InvalidOperationException(
                $"Trigger '{evt.TriggerName}' targets job '{evt.TargetJob}' but the Jobs capability is not enabled.");

        var arguments = new Dictionary<string, string>(evt.Metadata)
        {
            ["message"] = evt.Message,
            ["triggerId"] = evt.TriggerId,
        };

        var result = await runner.RunAsync(new JobRequest(evt.TargetJob!, arguments, TriggerId: evt.TriggerId), ct);
        if (!result.Ok)
            throw new InvalidOperationException($"Job '{evt.TargetJob}' failed: {result.Summary}");

        return result.Summary;
    }

    private async Task<string> RunUntrackedAsync(TriggerEvent evt, CancellationToken ct)
    {
        // A trigger naming a specific agent goes through handoff so persona-scoped
        // agents are resolved the same way they are for a user-initiated delegation.
        if (!string.IsNullOrWhiteSpace(evt.TargetAgent))
        {
            var handoff = _serviceProvider.GetService<IAgentHandoff>();
            if (handoff is not null)
            {
                var outcome = await handoff.HandoffAsync(evt.TargetAgent, evt.Message, ct);
                if (outcome.Status != "completed")
                    throw new InvalidOperationException($"Handoff to '{evt.TargetAgent}' failed: {outcome.Error}");

                return outcome.Response ?? string.Empty;
            }

            _logger.LogWarning(
                "Trigger {TriggerName} targets agent '{Agent}' but inter-agent communication is off, running the default agent",
                evt.TriggerName, evt.TargetAgent);
        }

        var agent = _serviceProvider.GetRequiredService<AIAgent>();
        var session = await agent.CreateSessionAsync(ct);
        var response = await agent.RunAsync(
            [new ChatMessage(ChatRole.User, evt.Message)], session, cancellationToken: ct);

        return response.Text ?? string.Empty;
    }
}
