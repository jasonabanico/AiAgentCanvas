namespace AiAgentCanvas.Abstractions;

public sealed record JobResult(bool Ok, string Summary, IReadOnlyDictionary<string, string>? Data = null)
{
    public static JobResult Success(string summary, IReadOnlyDictionary<string, string>? data = null) =>
        new(true, summary, data);

    /// <summary>A job that ran and found a problem, or could not do its work.</summary>
    public static JobResult Failure(string summary) => new(false, summary);
}

public sealed record JobInfo(string Name, string Description);

public sealed record JobRequest(
    string Name,
    IReadOnlyDictionary<string, string>? Arguments = null,
    string? TriggerId = null,
    string? TaskId = null);

public sealed class JobContext
{
    public JobContext(IServiceProvider services, IReadOnlyDictionary<string, string> arguments)
    {
        Services = services;
        Arguments = arguments;
    }

    public IServiceProvider Services { get; }

    public IReadOnlyDictionary<string, string> Arguments { get; }

    public string? Argument(string name) => Arguments.TryGetValue(name, out var value) ? value : null;
}

/// <summary>
/// A fixed, repeatable piece of work that runs without a model: fetch, filter, post,
/// check, report. Anything whose steps do not depend on judgment belongs here, because a
/// job costs no tokens, behaves the same each time, and can run on a schedule or in
/// response to a trigger like any agent task.
/// </summary>
/// <remarks>
/// Names are lowercase and hyphenated, such as <c>trigger-queue-health</c>. A job reports
/// a problem it found by returning <see cref="JobResult.Failure"/> and does not throw
/// for it; an exception means the job itself broke.
/// </remarks>
public interface IAgentJob
{
    string Name { get; }

    string Description { get; }

    Task<JobResult> RunAsync(JobContext context, CancellationToken ct);
}

/// <summary>Runs registered jobs with a run record, a timeout, and no overlap with itself.</summary>
public interface IJobRunner
{
    IReadOnlyList<JobInfo> List();

    Task<JobResult> RunAsync(JobRequest request, CancellationToken ct = default);
}
