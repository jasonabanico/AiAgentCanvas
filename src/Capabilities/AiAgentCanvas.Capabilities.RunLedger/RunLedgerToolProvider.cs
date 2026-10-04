#pragma warning disable MEAI001

using System.ComponentModel;
using System.Text.Json;
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Capabilities.RunLedger;

public static class RunLedgerToolProvider
{
    public static IReadOnlyList<AITool> CreateTools(IRunLedger ledger)
    {
        return
        [
            AIFunctionFactory.Create(
                [Description("List recent unattended agent runs (scheduled, trigger, handoff, job) with outcome, tokens and estimated cost. Status is one of Running, Succeeded, Failed, Cancelled, Abandoned")]
                (int? limit, string? agentName, string? status, string? source) =>
                {
                    var runs = ledger.Recent(new RunQuery(
                        Limit: limit ?? 10,
                        AgentName: agentName,
                        Status: Enum.TryParse<RunStatus>(status, true, out var s) ? s : null,
                        Source: Enum.TryParse<RunSource>(source, true, out var src) ? src : null,
                        TopLevelOnly: true));

                    return JsonSerializer.Serialize(runs.Select(Summarize));
                }, "list_recent_runs"),

            AIFunctionFactory.Create(
                [Description("Get one run in full: input, output, error, the tools it called and what it cost")]
                (string runId) =>
                {
                    var run = ledger.Get(runId);
                    return run is null
                        ? JsonSerializer.Serialize(new { error = $"No run with id '{runId}'" })
                        : JsonSerializer.Serialize(new
                        {
                            Summary = Summarize(run),
                            run.Input,
                            run.Output,
                            run.Error,
                            run.ToolCalls,
                        });
                }, "get_run"),

            AIFunctionFactory.Create(
                [Description("Totals for unattended runs over a time window: run count, failures, tokens and estimated spend")]
                (int? hoursBack, string? agentName) =>
                {
                    var since = DateTimeOffset.UtcNow.AddHours(-(hoursBack ?? 24));
                    var totals = ledger.Totals(since, agentName);
                    return JsonSerializer.Serialize(new
                    {
                        totals.Runs,
                        totals.Failed,
                        totals.InputTokens,
                        totals.OutputTokens,
                        totals.EstimatedCost,
                        Period = $"last {hoursBack ?? 24}h",
                    });
                }, "run_totals"),
        ];
    }

    private static object Summarize(RunRecord r) => new
    {
        r.Id,
        Source = r.Source.ToString(),
        r.AgentName,
        Status = r.Status.ToString(),
        StartedAt = r.StartedAt.ToString("u"),
        DurationSeconds = r.EndedAt is null ? (double?)null : Math.Round((r.EndedAt.Value - r.StartedAt).TotalSeconds, 1),
        r.InputTokens,
        r.OutputTokens,
        r.EstimatedCost,
        r.ToolCallCount,
        r.Termination,
        r.TriggerId,
        r.TaskId,
    };
}
