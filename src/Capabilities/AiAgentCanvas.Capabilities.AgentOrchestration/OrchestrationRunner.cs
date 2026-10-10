using System.Collections.Concurrent;
using System.Text.Json;
using AiAgentCanvas.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using WorkflowRunStatus = Microsoft.Agents.AI.Workflows.RunStatus;

namespace AiAgentCanvas.Capabilities.AgentOrchestration;

public sealed class OrchestrationException(string message) : Exception(message);

/// <summary>A person's answer to a run that stopped for input.</summary>
/// <param name="Approve">Accept what the run proposed as it stands.</param>
/// <param name="Feedback">What to change. Required when <paramref name="Approve"/> is false.</param>
public sealed record OrchestrationResponse(bool Approve, string? Feedback = null);

/// <summary>
/// Runs several agents as a group chat, a handoff chain or a Magentic team, and keeps the run
/// durable. Each step is checkpointed to disk. A run that needs a person, such as a Magentic
/// plan awaiting sign-off, stops, records what it is asking, and ends the process work
/// entirely. When the person answers, possibly days later and after a restart, the run is
/// rebuilt from its record, restored from the checkpoint, and continues where it stopped.
/// </summary>
public sealed class OrchestrationRunner
{
    private readonly OrchestrationStore _store;
    private readonly Func<string, AIAgent?> _resolve;
    private readonly OrchestrationOptions _options;
    private readonly INotificationSink? _sink;
    private readonly IRunLedger? _ledger;
    private readonly ILogger<OrchestrationRunner> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new();

    public OrchestrationRunner(
        OrchestrationStore store,
        Func<string, AIAgent?> resolve,
        OrchestrationOptions options,
        ILogger<OrchestrationRunner> logger,
        INotificationSink? sink = null,
        IRunLedger? ledger = null)
    {
        _store = store;
        _resolve = resolve;
        _options = options;
        _logger = logger;
        _sink = sink;
        _ledger = ledger;
    }

    public string CheckpointPath(string runId) =>
        Path.Combine(
            Path.IsPathRooted(_options.CheckpointDirectory)
                ? _options.CheckpointDirectory
                : Path.Combine(Directory.GetCurrentDirectory(), _options.CheckpointDirectory),
            runId);

    // ---- public operations ----

    public async Task<OrchestrationRun> StartAsync(OrchestrationSpec spec, CancellationToken ct = default)
    {
        var normalized = Normalize(spec);
        var run = new OrchestrationRun { Spec = normalized, Status = OrchestrationStatus.Running };
        _store.Save(run);

        await ExecuteAsync(run, response: null, ct);
        return _store.Get(run.Id)!;
    }

    public async Task<OrchestrationRun> RespondAsync(string runId, OrchestrationResponse response, CancellationToken ct = default)
    {
        var run = _store.Get(runId) ?? throw new OrchestrationException($"No orchestration run '{runId}'.");

        if (run.Status != OrchestrationStatus.WaitingForInput || run.Pending is null)
            throw new OrchestrationException($"Run '{runId}' is {run.Status} and is not waiting for an answer.");

        if (!response.Approve && string.IsNullOrWhiteSpace(response.Feedback))
            throw new OrchestrationException("Say what to change, or approve.");

        await ExecuteAsync(run, response, ct);
        return _store.Get(runId)!;
    }

    /// <summary>Continues a run that a restart cut short, from its last checkpoint.</summary>
    public async Task<OrchestrationRun> ResumeAsync(string runId, CancellationToken ct = default)
    {
        var run = _store.Get(runId) ?? throw new OrchestrationException($"No orchestration run '{runId}'.");

        if (run.Status != OrchestrationStatus.Interrupted)
            throw new OrchestrationException($"Run '{runId}' is {run.Status}. Only an interrupted run is resumed this way.");

        await ExecuteAsync(run, response: null, ct, resume: true);
        return _store.Get(runId)!;
    }

    public OrchestrationRun? Cancel(string runId)
    {
        var run = _store.Get(runId);
        if (run is null)
            return null;

        if (_active.TryGetValue(runId, out var cts))
            cts.Cancel();

        if (run.Status is OrchestrationStatus.WaitingForInput or OrchestrationStatus.Interrupted)
        {
            run.Status = OrchestrationStatus.Cancelled;
            run.Pending = null;
            _store.Save(run);
        }

        return _store.Get(runId);
    }

    // ---- execution ----

    private async Task ExecuteAsync(OrchestrationRun run, OrchestrationResponse? response, CancellationToken ct, bool resume = false)
    {
        var gate = _locks.GetOrAdd(run.Id, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, ct))
            throw new OrchestrationException($"Run '{run.Id}' is already being worked on.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, _options.RunTimeoutMinutes)));
        _active[run.Id] = cts;

        try
        {
            run.Status = OrchestrationStatus.Running;
            run.Error = null;
            _store.Save(run);

            var start = new RunStart(RunSource.Orchestration, $"{run.Spec.Kind}", Truncate(run.Spec.Task, 2000));
            await RunTracking.RunAsync(_ledger, start, async token =>
            {
                await DriveAsync(run, response, resume, token);
                return run.Result ?? run.Pending?.Summary ?? run.Error ?? run.Status.ToString();
            }, cts.Token, ex => _logger.LogWarning(ex, "Could not record orchestration run {Run} in the ledger", run.Id));
        }
        catch (OperationCanceledException)
        {
            // Cancelled by a caller or by the run timeout. A person-requested cancel was
            // already recorded, so only mark a run that is still marked running.
            var current = _store.Get(run.Id);
            if (current is { Status: OrchestrationStatus.Running })
            {
                current.Status = cts.IsCancellationRequested && !ct.IsCancellationRequested
                    ? OrchestrationStatus.Failed
                    : OrchestrationStatus.Cancelled;
                current.Error = ct.IsCancellationRequested ? "Cancelled." : $"The run exceeded {_options.RunTimeoutMinutes} minutes.";
                _store.Save(current);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Orchestration run {Run} failed", run.Id);
            var current = _store.Get(run.Id) ?? run;
            current.Status = OrchestrationStatus.Failed;
            current.Error = ex.Message;
            current.Pending = null;
            _store.Save(current);
        }
        finally
        {
            _active.TryRemove(run.Id, out _);
            gate.Release();
        }

        var finished = _store.Get(run.Id)!;
        if (finished.Status == OrchestrationStatus.WaitingForInput)
            await NotifyAsync(finished, ct);
    }

    private async Task DriveAsync(OrchestrationRun run, OrchestrationResponse? response, bool resume, CancellationToken ct)
    {
        var workflow = Build(run.Spec);

        Directory.CreateDirectory(CheckpointPath(run.Id));
        using var store = new FileSystemJsonCheckpointStore(new DirectoryInfo(CheckpointPath(run.Id)));
        var manager = CheckpointManager.CreateJson(store);

        Run workflowRun;
        if (response is null && !resume)
        {
            workflowRun = await InProcessExecution.RunAsync(
                workflow, new ChatMessage(ChatRole.User, run.Spec.Task), manager, run.Id, ct);
        }
        else
        {
            var checkpoint = await manager.GetLatestCheckpointAsync(run.Id, ct)
                ?? throw new OrchestrationException("The run has no checkpoint to continue from.");

            workflowRun = await InProcessExecution.ResumeAsync(workflow, checkpoint, manager, ct);

            if (response is not null)
            {
                var request = FindRequest(workflowRun, run.Pending!.RequestId)
                    ?? throw new OrchestrationException("The run no longer has the request it was waiting on.");

                var answer = new MagenticPlanReviewResponse(
                    response.Approve ? [] : [new ChatMessage(ChatRole.User, response.Feedback!)]);

                await workflowRun.ResumeAsync(ct, [request.CreateResponse(answer)]);
                run.Pending = null;
            }
        }

        await using (workflowRun)
        {
            await CollectAsync(run, workflowRun, ct);
        }

        _store.Save(run);
    }

    private static ExternalRequest? FindRequest(Run workflowRun, string requestId) =>
        workflowRun.OutgoingEvents
            .OfType<RequestInfoEvent>()
            .Select(e => e.Request)
            .FirstOrDefault(r => r.RequestId == requestId);

    private async Task CollectAsync(OrchestrationRun run, Run workflowRun, CancellationToken ct)
    {
        string? output = null;
        List<TranscriptEntry>? fromOutput = null;
        var turns = new TurnCollector();
        var events = workflowRun.OutgoingEvents.ToList();

        foreach (var evt in events)
        {
            switch (evt)
            {
                case AgentResponseEvent agent when !string.IsNullOrWhiteSpace(agent.Response.Text):
                    turns.Add(agent.Response.Messages.LastOrDefault()?.AuthorName ?? agent.ExecutorId, agent.Response.ResponseId, agent.Response.Text, replace: true);
                    break;

                case AgentResponseUpdateEvent streamed when !string.IsNullOrEmpty(streamed.Update.Text):
                    turns.Add(streamed.Update.AuthorName ?? streamed.ExecutorId, streamed.Update.ResponseId ?? streamed.Update.MessageId, streamed.Update.Text, replace: false);
                    break;

                case WorkflowOutputEvent finished:
                    output = TextOf(finished.Data) ?? output;

                    // A group chat, handoff or Magentic run ends by yielding the whole
                    // conversation with each message's author, which is the best transcript.
                    if (finished.Data is IEnumerable<ChatMessage> conversation)
                    {
                        fromOutput = conversation
                            .Where(m => m.Role != ChatRole.User && !string.IsNullOrWhiteSpace(m.Text))
                            .Select(m => new TranscriptEntry(m.AuthorName ?? "agent", m.Text))
                            .ToList();
                    }
                    break;

                case WorkflowErrorEvent error:
                    throw new OrchestrationException(error.Exception?.Message ?? "The workflow reported an error.");

                case ExecutorFailedEvent failed:
                    throw new OrchestrationException($"{failed.ExecutorId} failed: {(failed.Data as Exception)?.Message ?? "no detail"}");
            }
        }

        // The same events come back after a resume. Keep only the entries this pass added once.
        run.Transcript = MergeTranscript(run.Transcript, turns.Entries, fromOutput);

        var status = await workflowRun.GetStatusAsync(ct);
        var pending = events.OfType<RequestInfoEvent>().Select(e => e.Request).LastOrDefault();

        if (status == WorkflowRunStatus.PendingRequests && pending is not null)
        {
            run.Status = OrchestrationStatus.WaitingForInput;
            run.Pending = Describe(pending);
            return;
        }

        run.Pending = null;
        run.Status = OrchestrationStatus.Completed;
        run.Result = output ?? run.Transcript.LastOrDefault()?.Text;
    }

    private static PendingInput Describe(ExternalRequest request)
    {
        if (request.TryGetDataAs<MagenticPlanReviewRequest>(out var plan) && plan is not null)
        {
            var summary = plan.Plan?.Text ?? "The manager proposes a plan.";
            if (plan.IsStalled)
                summary = "The team is stalled and the manager proposes a new plan.\n\n" + summary;
            return new PendingInput(request.RequestId, "plan_review", summary, null);
        }

        return new PendingInput(request.RequestId, "input", "The run is waiting for input this system does not know how to answer.", null);
    }

    /// <summary>
    /// A group chat or handoff yields the whole conversation at the end, which is the best
    /// transcript. A Magentic run yields only the manager's final answer, so the turns the
    /// team took come from the streamed updates, with the final answer added last.
    /// </summary>
    private static List<TranscriptEntry> MergeTranscript(
        List<TranscriptEntry> earlier, List<TranscriptEntry> turns, List<TranscriptEntry>? final)
    {
        if (final is not null && final.Count >= turns.Count)
            return final;

        var merged = new List<TranscriptEntry>(earlier);
        merged.AddRange(turns);

        if (final is not null)
        {
            foreach (var entry in final)
            {
                if (!merged.Any(m => m.Agent == entry.Agent && m.Text == entry.Text))
                    merged.Add(entry);
            }
        }

        return merged;
    }

    /// <summary>Joins streamed fragments into one entry per agent message, in the order they began.</summary>
    private sealed class TurnCollector
    {
        private readonly List<(string Agent, string? Key, System.Text.StringBuilder Text)> _turns = [];

        public void Add(string agent, string? key, string text, bool replace)
        {
            var index = key is null ? -1 : _turns.FindIndex(t => t.Agent == agent && t.Key == key);
            if (index < 0)
            {
                _turns.Add((agent, key, new System.Text.StringBuilder(text)));
                return;
            }

            if (replace)
                _turns[index].Text.Clear();
            _turns[index].Text.Append(text);
        }

        public List<TranscriptEntry> Entries =>
            _turns.Select(t => new TranscriptEntry(t.Agent, t.Text.ToString())).Where(e => !string.IsNullOrWhiteSpace(e.Text)).ToList();
    }

    private static string? TextOf(object? data) => data switch
    {
        null => null,
        string s => s,
        ChatMessage m => m.Text,
        AgentResponse r => r.Text,
        IEnumerable<ChatMessage> messages => messages.LastOrDefault(m => !string.IsNullOrWhiteSpace(m.Text))?.Text,
        _ => data.ToString(),
    };

    // ---- building ----

    private OrchestrationSpec Normalize(OrchestrationSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Task))
            throw new OrchestrationException("Say what the agents should work on.");

        var agents = spec.Agents.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // The lead is the first agent of a handoff and the manager of a Magentic team. A group
        // chat has no lead.
        string? lead = spec.Kind switch
        {
            OrchestrationKind.GroupChat => null,
            OrchestrationKind.Handoff => string.IsNullOrWhiteSpace(spec.Lead) ? agents.FirstOrDefault() : spec.Lead.Trim(),
            _ => string.IsNullOrWhiteSpace(spec.Lead) ? "default" : spec.Lead.Trim(),
        };

        if (spec.Kind == OrchestrationKind.Handoff && lead is not null && !agents.Contains(lead, StringComparer.OrdinalIgnoreCase))
            agents.Insert(0, lead);

        var needed = spec.Kind == OrchestrationKind.Magentic ? 1 : 2;
        if (agents.Count < needed)
            throw new OrchestrationException(spec.Kind == OrchestrationKind.Magentic
                ? "Name at least one agent for the team."
                : "Name at least two agents.");

        if (agents.Count > _options.MaxAgents)
            throw new OrchestrationException($"{agents.Count} agents were named. The limit is {_options.MaxAgents}.");

        foreach (var name in lead is null ? agents : agents.Append(lead))
        {
            if (_resolve(name) is null)
                throw new OrchestrationException($"No agent named '{name}'.");
        }

        var rounds = Math.Clamp(spec.MaxRounds ?? _options.DefaultMaxRounds, 1, Math.Max(1, _options.MaxRoundsCap));
        return spec with { Agents = agents, MaxRounds = rounds, Lead = lead };
    }

    private Workflow Build(OrchestrationSpec spec)
    {
        var team = spec.Agents.Select(n => _resolve(n) ?? throw new OrchestrationException($"No agent named '{n}'.")).ToList();
        var lead = spec.Lead is null ? null : _resolve(spec.Lead) ?? throw new OrchestrationException($"No agent named '{spec.Lead}'.");
        var rounds = spec.MaxRounds ?? _options.DefaultMaxRounds;

        switch (spec.Kind)
        {
            case OrchestrationKind.GroupChat:
                return AgentWorkflowBuilder
                    .CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents) { MaximumIterationCount = rounds })
                    .AddParticipants(team)
                    .Build();

            case OrchestrationKind.Handoff:
            {
                var specialists = team.Where(a => !ReferenceEquals(a, lead)).ToList();
                return AgentWorkflowBuilder
                    .CreateHandoffBuilderWith(lead!)
                    .WithHandoffs(lead!, specialists)
                    .WithHandoffs(specialists, lead!)
                    .Build();
            }

            default:
                return AgentWorkflowBuilder
                    .CreateMagenticBuilderWith(lead!)
                    .AddParticipants(team)
                    .WithMaxRounds(rounds)
                    .RequirePlanSignoff(spec.RequireSignoff)
                    .Build();
        }
    }

    // ---- housekeeping ----

    public int PruneOld()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, _options.RetentionDays));
        var removed = _store.PruneFinished(cutoff);
        foreach (var id in removed)
        {
            try
            {
                var path = CheckpointPath(id);
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch (IOException ex)
            {
                _logger.LogDebug(ex, "Could not remove checkpoints for run {Run}", id);
            }
        }
        return removed.Count;
    }

    private async Task NotifyAsync(OrchestrationRun run, CancellationToken ct)
    {
        if (_sink is null || run.Pending is null)
            return;

        try
        {
            await _sink.SendAsync(new AgentNotification
            {
                Title = "A team of agents needs your decision",
                Body = $"{run.Spec.Kind} run {run.Id} is waiting: {Truncate(run.Pending.Summary, 500)}",
                Source = $"orchestration:{run.Id}",
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send the notification for orchestration run {Run}", run.Id);
        }
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "...";
}
