using System.Diagnostics;
using System.Threading.Channels;
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

    /// <summary>How often the evaluation loop checks scheduled triggers.</summary>
    public int PollSeconds { get; set; } = 30;

    /// <summary>
    /// Pending events held before the producer blocks. A bounded queue is the point:
    /// an unbounded one turns a trigger storm into a memory leak.
    /// </summary>
    public int QueueCapacity { get; set; } = 256;

    /// <summary>Wall-clock ceiling on the agent run a single trigger starts.</summary>
    public int RunTimeoutMinutes { get; set; } = 10;
}

/// <summary>
/// Consumes fired triggers and runs the agent for each one. This is the half of the
/// event-trigger feature that turns a queued event into work: without it triggers
/// fire into a queue nothing reads.
/// </summary>
public sealed class TriggerDispatchService : BackgroundService
{
    private readonly Channel<TriggerEvent> _channel;
    private readonly IServiceProvider _serviceProvider;
    private readonly EventTriggerOptions _options;
    private readonly ILogger<TriggerDispatchService> _logger;

    public TriggerDispatchService(
        Channel<TriggerEvent> channel,
        IServiceProvider serviceProvider,
        EventTriggerOptions options,
        ILogger<TriggerDispatchService> logger)
    {
        _channel = channel;
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Trigger dispatcher started");

        await foreach (var evt in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await DispatchAsync(evt, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dispatch failed for trigger {TriggerId} ({TriggerName})",
                    evt.TriggerId, evt.TriggerName);
            }
        }
    }

    private async Task DispatchAsync(TriggerEvent evt, CancellationToken stoppingToken)
    {
        using var activity = AgentTelemetry.Source.StartActivity("trigger dispatch", ActivityKind.Internal);
        activity?.SetTag("trigger.id", evt.TriggerId);
        activity?.SetTag("trigger.name", evt.TriggerName);
        activity?.SetTag("trigger.target_agent", evt.TargetAgent ?? "default");

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

    private async Task<string> RunAsync(TriggerEvent evt, CancellationToken ct)
    {
        // A trigger naming a specific agent goes through handoff so persona-scoped
        // agents are resolved the same way they are for a user-initiated delegation.
        if (!string.IsNullOrWhiteSpace(evt.TargetAgent))
        {
            var handoff = _serviceProvider.GetService<IAgentHandoff>();
            if (handoff is not null)
            {
                var outcome = await handoff.HandoffAsync(evt.TargetAgent, evt.Message, ct);
                return outcome.Status == "completed"
                    ? outcome.Response ?? string.Empty
                    : $"Handoff to '{evt.TargetAgent}' failed: {outcome.Error}";
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
