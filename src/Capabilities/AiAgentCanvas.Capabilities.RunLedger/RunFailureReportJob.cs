using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.Capabilities.RunLedger;

/// <summary>
/// Summarizes unattended runs that failed or were left unfinished, and sends one
/// notification when there are any. Schedule it daily to see overnight failures.
/// </summary>
public sealed class RunFailureReportJob : IAgentJob
{
    private readonly IRunLedger _ledger;

    public RunFailureReportJob(IRunLedger ledger) => _ledger = ledger;

    public string Name => "run-failure-report";

    public string Description =>
        "Summarizes unattended runs that failed or were abandoned in the last day (argument: hours) and notifies when there are any";

    public async Task<JobResult> RunAsync(JobContext context, CancellationToken ct)
    {
        var hours = int.TryParse(context.Argument("hours"), out var parsed) ? Math.Clamp(parsed, 1, 168) : 24;
        var since = DateTimeOffset.UtcNow.AddHours(-hours);

        var failed = _ledger.Recent(new RunQuery(Limit: 200, Since: since, Status: RunStatus.Failed, TopLevelOnly: true));
        var abandoned = _ledger.Recent(new RunQuery(Limit: 200, Since: since, Status: RunStatus.Abandoned, TopLevelOnly: true));
        var totals = _ledger.Totals(since);

        if (failed.Count == 0 && abandoned.Count == 0)
        {
            return JobResult.Success(
                $"No failed runs in the last {hours}h: {totals.Runs} run(s), estimated spend {totals.EstimatedCost:F2}.");
        }

        var byAgent = failed.Concat(abandoned)
            .GroupBy(r => r.AgentName)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key} x{g.Count()}");

        var latest = failed.Concat(abandoned).OrderByDescending(r => r.StartedAt).First();
        var summary =
            $"{failed.Count} failed and {abandoned.Count} abandoned run(s) in the last {hours}h of {totals.Runs} total. "
            + $"By agent: {string.Join(", ", byAgent)}. Most recent: {latest.AgentName} ({latest.Status}), "
            + $"{latest.Error ?? "no error recorded"}";

        var sink = context.Services.GetService<INotificationSink>();
        if (sink is not null)
        {
            await sink.SendAsync(new AgentNotification
            {
                Title = $"{failed.Count + abandoned.Count} unattended run(s) failed",
                Body = summary,
                Source = $"job:{Name}",
            }, ct);
        }

        return JobResult.Success(summary, new Dictionary<string, string>
        {
            ["failed"] = failed.Count.ToString(),
            ["abandoned"] = abandoned.Count.ToString(),
            ["total"] = totals.Runs.ToString(),
        });
    }
}
