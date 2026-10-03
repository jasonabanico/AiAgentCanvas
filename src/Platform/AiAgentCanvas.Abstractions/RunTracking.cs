namespace AiAgentCanvas.Abstractions;

public sealed record RunStart(
    RunSource Source,
    string AgentName,
    string? Input = null,
    string? TriggerId = null,
    string? TaskId = null);

/// <summary>
/// Wraps a unit of agent work so it has an ambient <see cref="AgentRunContext"/> and,
/// when a ledger is registered, a record. Works without a ledger, because the context
/// is also what the per-run cost limit reads.
/// </summary>
public static class RunTracking
{
    public static async Task<string> RunAsync(
        IRunLedger? ledger,
        RunStart start,
        Func<CancellationToken, Task<string>> work,
        CancellationToken ct = default,
        Action<Exception>? onLedgerError = null)
    {
        var context = new AgentRunContext(
            Guid.NewGuid().ToString("N"),
            start.Source,
            start.AgentName,
            AgentRunContext.Current,
            start.TriggerId,
            start.TaskId);

        using var scope = context.Enter();

        Guard(() => ledger?.Start(new RunRecord
        {
            Id = context.RunId,
            ParentId = context.Parent?.RunId,
            Source = start.Source,
            AgentName = start.AgentName,
            TriggerId = start.TriggerId,
            TaskId = start.TaskId,
            StartedAt = context.StartedAt,
            Status = RunStatus.Running,
            Input = start.Input,
        }), onLedgerError);

        try
        {
            var output = await work(ct).ConfigureAwait(false);
            Finish(RunStatus.Succeeded, output, null);
            return output;
        }
        catch (OperationCanceledException)
        {
            Finish(RunStatus.Cancelled, null, "cancelled");
            throw;
        }
        catch (Exception ex)
        {
            Finish(RunStatus.Failed, null, ex.Message);
            throw;
        }

        void Finish(RunStatus status, string? output, string? error) =>
            Guard(() => ledger?.Finish(context.RunId, status, output, error, context.Snapshot(), DateTimeOffset.UtcNow), onLedgerError);
    }

    /// <summary>A ledger failure must not fail the run it is describing.</summary>
    private static void Guard(Action action, Action<Exception>? onError)
    {
        try { action(); }
        catch (Exception ex) { onError?.Invoke(ex); }
    }
}
