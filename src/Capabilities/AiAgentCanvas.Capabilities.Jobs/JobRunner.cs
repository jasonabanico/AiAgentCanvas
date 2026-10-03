using System.Diagnostics;
using System.Text.RegularExpressions;
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.Jobs;

public sealed class JobOptions
{
    public const string SectionName = "Agent:Jobs";

    /// <summary>Wall-clock ceiling on one job run.</summary>
    public int TimeoutSeconds { get; set; } = 600;
}

/// <summary>
/// Runs jobs. Each run gets a ledger record and a timeout. A job does not overlap with
/// itself: if a scheduled job is still running when its next run comes due, the new run
/// is skipped, not stacked behind it.
/// </summary>
public sealed partial class JobRunner : IJobRunner
{
    private static readonly IReadOnlyDictionary<string, string> NoArguments = new Dictionary<string, string>();

    private readonly Dictionary<string, IAgentJob> _jobs;
    private readonly Dictionary<string, SemaphoreSlim> _gates;
    private readonly IServiceProvider _services;
    private readonly IRunLedger? _ledger;
    private readonly JobOptions _options;
    private readonly ILogger<JobRunner> _logger;

    public JobRunner(
        IEnumerable<IAgentJob> jobs,
        IServiceProvider services,
        JobOptions options,
        ILogger<JobRunner> logger,
        IRunLedger? ledger = null)
    {
        _services = services;
        _options = options;
        _logger = logger;
        _ledger = ledger;

        var list = jobs.ToList();

        foreach (var job in list.Where(j => !ValidName().IsMatch(j.Name)))
        {
            throw new InvalidOperationException(
                $"Job name '{job.Name}' is not valid. Use lowercase letters, digits and hyphens, such as 'trigger-queue-health'.");
        }

        var duplicates = list.GroupBy(j => j.Name, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            throw new InvalidOperationException($"More than one job is registered under the name(s): {string.Join(", ", duplicates)}.");

        _jobs = list.ToDictionary(j => j.Name, StringComparer.Ordinal);
        _gates = list.ToDictionary(j => j.Name, _ => new SemaphoreSlim(1, 1), StringComparer.Ordinal);
    }

    public IReadOnlyList<JobInfo> List() =>
        _jobs.Values.OrderBy(j => j.Name, StringComparer.Ordinal).Select(j => new JobInfo(j.Name, j.Description)).ToList();

    public async Task<JobResult> RunAsync(JobRequest request, CancellationToken ct = default)
    {
        if (!_jobs.TryGetValue(request.Name, out var job))
        {
            var known = _jobs.Count == 0 ? "none are registered" : string.Join(", ", _jobs.Keys.Order());
            Count(request.Name, "unknown");
            return JobResult.Failure($"No job named '{request.Name}'. Available: {known}.");
        }

        var gate = _gates[job.Name];
        if (!await gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            Count(job.Name, "skipped");
            _logger.LogInformation("Job {Job} is still running from an earlier trigger, skipping this run", job.Name);
            return JobResult.Success("Skipped: a previous run of this job is still in progress.");
        }

        try
        {
            return await ExecuteAsync(job, request, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<JobResult> ExecuteAsync(IAgentJob job, JobRequest request, CancellationToken ct)
    {
        var arguments = request.Arguments ?? NoArguments;
        JobResult? result = null;

        using var activity = AgentTelemetry.Source.StartActivity($"job {job.Name}", ActivityKind.Internal);
        activity?.SetTag("job.name", job.Name);

        try
        {
            await RunTracking.RunAsync(
                _ledger,
                new RunStart(RunSource.Job, job.Name, Describe(arguments), request.TriggerId, request.TaskId),
                async token =>
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));

                    try
                    {
                        result = await job.RunAsync(new JobContext(_services, arguments), timeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (timeout.IsCancellationRequested && !token.IsCancellationRequested)
                    {
                        throw new TimeoutException($"timed out after {_options.TimeoutSeconds} seconds");
                    }

                    if (!result.Ok)
                        throw new JobFailedException(result.Summary);

                    return result.Summary;
                },
                ct,
                ex => _logger.LogWarning(ex, "Run ledger write failed for job {Job}", job.Name)).ConfigureAwait(false);

            Count(job.Name, "ok");
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (JobFailedException)
        {
            // The job ran and reported a problem. Its own result carries the detail.
            Count(job.Name, "failed");
            activity?.SetStatus(ActivityStatusCode.Error, result?.Summary);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Count(job.Name, "error");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Job {Job} failed", job.Name);
            return JobResult.Failure(ex.Message);
        }

        return result ?? JobResult.Failure("The job returned no result.");
    }

    private static string? Describe(IReadOnlyDictionary<string, string> arguments) =>
        arguments.Count == 0 ? null : string.Join(", ", arguments.Select(a => $"{a.Key}={a.Value}"));

    private static void Count(string job, string outcome) =>
        AgentTelemetry.JobRuns.Add(1,
            new KeyValuePair<string, object?>("job", job),
            new KeyValuePair<string, object?>("outcome", outcome));

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,62}$")]
    private static partial Regex ValidName();

    private sealed class JobFailedException(string message) : Exception(message);
}
