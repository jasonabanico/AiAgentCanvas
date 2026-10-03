using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.Capabilities.EventTriggers;

/// <summary>
/// Reports trigger events that failed every attempt. Dead events wait for a person, so a
/// scheduled run of this job is what makes sure someone is told.
/// </summary>
public sealed class TriggerQueueHealthJob : IAgentJob
{
    private readonly TriggerEventQueue _queue;

    public TriggerQueueHealthJob(TriggerEventQueue queue) => _queue = queue;

    public string Name => "trigger-queue-health";

    public string Description =>
        "Checks the trigger event queue and sends a notification when events have failed every attempt and are parked";

    public async Task<JobResult> RunAsync(JobContext context, CancellationToken ct)
    {
        var counts = _queue.Counts();

        if (counts.Dead == 0)
        {
            return JobResult.Success(
                $"Trigger queue healthy: {counts.Pending} waiting, {counts.Running} running, none parked.",
                new Dictionary<string, string>
                {
                    ["pending"] = counts.Pending.ToString(),
                    ["running"] = counts.Running.ToString(),
                    ["dead"] = "0",
                });
        }

        var examples = string.Join(" | ", _queue.Dead(5).Select(e => $"{e.TriggerName}: {e.LastError}"));
        var summary = $"{counts.Dead} trigger event(s) failed every attempt and are parked. Examples: {examples}";

        var sink = context.Services.GetService<INotificationSink>();
        if (sink is not null)
        {
            await sink.SendAsync(new AgentNotification
            {
                Title = "Trigger events need attention",
                Body = summary,
                Source = $"job:{Name}",
            }, ct);
        }

        return JobResult.Failure(summary);
    }
}
