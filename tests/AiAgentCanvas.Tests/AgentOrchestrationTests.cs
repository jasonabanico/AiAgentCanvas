#pragma warning disable MEAI001, MAAI001

using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Capabilities.AgentOrchestration;
using AiAgentCanvas.Capabilities.RunLedger;
using Microsoft.Agents.AI;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiAgentCanvas.Tests;

public abstract class OrchestrationTestBase : IDisposable
{
    protected readonly string Root = Path.Combine(Path.GetTempPath(), $"orch-{Guid.NewGuid():N}");
    protected readonly Dictionary<string, AIAgent> Agents = new(StringComparer.OrdinalIgnoreCase);
    protected readonly Dictionary<string, ScriptedChatClient> Clients = new(StringComparer.OrdinalIgnoreCase);
    protected readonly List<AgentNotification> Notifications = [];

    protected OrchestrationTestBase() => Directory.CreateDirectory(Root);

    protected ScriptedChatClient AddAgent(string name, params string[] replies)
    {
        var client = new ScriptedChatClient(replies);
        Clients[name] = client;
        Agents[name] = new ChatClientAgent(client, instructions: $"You are {name}.", name: name);
        return client;
    }

    private sealed class CollectingSink(List<AgentNotification> into) : INotificationSink
    {
        public Task SendAsync(AgentNotification notification, CancellationToken ct = default)
        {
            into.Add(notification);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<AgentNotification> SubscribeAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    protected OrchestrationOptions Options(Action<OrchestrationOptions>? configure = null)
    {
        var options = new OrchestrationOptions
        {
            DatabasePath = Path.Combine(Root, "orch.db"),
            CheckpointDirectory = Path.Combine(Root, "checkpoints"),
            RunTimeoutMinutes = 2,
        };
        configure?.Invoke(options);
        return options;
    }

    protected OrchestrationRunner Runner(OrchestrationOptions? options = null, IRunLedger? ledger = null)
    {
        options ??= Options();
        return new OrchestrationRunner(
            new OrchestrationStore(options.DatabasePath),
            name => Agents.GetValueOrDefault(name),
            options,
            NullLogger<OrchestrationRunner>.Instance,
            new CollectingSink(Notifications),
            ledger);
    }

    protected OrchestrationRun? Stored(string id) => new OrchestrationStore(Options().DatabasePath).Get(id);

    protected void AgeAllRuns(string databasePath, int days)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE orchestration_runs SET updated_at = $old";
        cmd.Parameters.AddWithValue("$old", DateTimeOffset.UtcNow.AddDays(-days).ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Root, true); } catch (IOException) { }
    }
}

public class GroupChatOrchestrationTests : OrchestrationTestBase
{
    [Fact]
    public async Task Agents_take_turns_until_the_round_limit_and_the_run_completes()
    {
        AddAgent("writer", "Draft one.", "Draft two.");
        AddAgent("critic", "Needs work.", "Looks good.");

        var run = await Runner().StartAsync(new OrchestrationSpec(
            OrchestrationKind.GroupChat, "Write a slogan.", ["writer", "critic"], MaxRounds: 4));

        Assert.Equal(OrchestrationStatus.Completed, run.Status);
        Assert.Equal(["writer", "critic", "writer", "critic"], run.Transcript.Select(t => t.Agent));
        Assert.Equal("Looks good.", run.Result);
    }
}

public class MagenticOrchestrationTests : OrchestrationTestBase
{
    private static string Ledger(bool satisfied, string next, string instruction) =>
        $$$"""
        {"is_request_satisfied":{"answer":{{{satisfied.ToString().ToLowerInvariant()}}},"reason":"r"},
         "is_in_loop":{"answer":false,"reason":"r"},
         "is_progress_being_made":{"answer":true,"reason":"r"},
         "next_speaker":{"answer":"{{{next}}}","reason":"r"},
         "instruction_or_question":{"answer":"{{{instruction}}}","reason":"r"}}
        """;

    private (ScriptedChatClient Manager, ScriptedChatClient Researcher) Team(params string[] extraManagerReplies)
    {
        string[] replies =
        [
            "Facts: none known.",
            "Plan: 1. researcher gathers notes. 2. writer summarises.",
            .. extraManagerReplies,
        ];
        var manager = AddAgent("manager", replies);
        var researcher = AddAgent("researcher", "Notes on the topic.");
        AddAgent("writer", "A short summary.");
        return (manager, researcher);
    }

    private static string[] HappyPath() =>
    [
        Ledger(false, "researcher", "Gather notes."),
        Ledger(false, "writer", "Summarise the notes."),
        Ledger(true, "writer", "Done."),
        "Final answer: the topic is summarised.",
    ];

    private static OrchestrationSpec Spec(bool signoff = true) =>
        new(OrchestrationKind.Magentic, "Summarise the topic.", ["researcher", "writer"], Lead: "manager", MaxRounds: 6, RequireSignoff: signoff);

    [Fact]
    public async Task A_run_that_needs_signoff_stops_at_the_plan_and_records_what_it_asks()
    {
        Team();

        var run = await Runner().StartAsync(Spec());

        Assert.Equal(OrchestrationStatus.WaitingForInput, run.Status);
        Assert.Equal("plan_review", run.Pending!.Kind);
        Assert.Contains("researcher", run.Pending.Summary);
        Assert.Contains(Notifications, n => n.Source == $"orchestration:{run.Id}");
    }

    [Fact]
    public async Task Approving_the_plan_lets_the_team_finish_the_work()
    {
        Team(HappyPath());
        var runner = Runner();
        var waiting = await runner.StartAsync(Spec());

        var done = await runner.RespondAsync(waiting.Id, new OrchestrationResponse(Approve: true));

        Assert.Equal(OrchestrationStatus.Completed, done.Status);
        Assert.Null(done.Pending);
        Assert.Contains(done.Transcript, t => t.Agent == "researcher" && t.Text.Contains("Notes"));
        Assert.Contains(done.Transcript, t => t.Agent == "writer" && t.Text.Contains("summary"));
        Assert.Contains("summarised", done.Result);
    }

    [Fact]
    public async Task Asking_for_changes_sends_the_feedback_to_the_manager_and_returns_a_revised_plan()
    {
        var (manager, _) = Team("Plan v2: researcher only.");
        var runner = Runner();
        var waiting = await runner.StartAsync(Spec());

        var revised = await runner.RespondAsync(waiting.Id, new OrchestrationResponse(false, "Skip the writer."));

        Assert.Equal(OrchestrationStatus.WaitingForInput, revised.Status);
        Assert.Contains("Plan v2", revised.Pending!.Summary);
        Assert.Contains(manager.Calls.SelectMany(c => c), m => m.Text.Contains("Skip the writer."));
    }

    [Fact]
    public async Task A_run_waiting_for_a_person_survives_a_restart()
    {
        Team(HappyPath());
        var waiting = await Runner().StartAsync(Spec());

        // A new runner over the same database and checkpoint folder, as after a restart.
        var done = await Runner().RespondAsync(waiting.Id, new OrchestrationResponse(true));

        Assert.Equal(OrchestrationStatus.Completed, done.Status);
    }

    [Fact]
    public async Task Without_signoff_the_run_goes_straight_through()
    {
        Team(HappyPath());

        var run = await Runner().StartAsync(Spec(signoff: false));

        Assert.Equal(OrchestrationStatus.Completed, run.Status);
        Assert.Contains("summarised", run.Result);
    }

    [Fact]
    public async Task Only_a_run_that_is_waiting_can_be_answered()
    {
        Team(HappyPath());
        var runner = Runner();
        var done = await runner.StartAsync(Spec(signoff: false));

        var ex = await Assert.ThrowsAsync<OrchestrationException>(() => runner.RespondAsync(done.Id, new OrchestrationResponse(true)));

        Assert.Contains("not waiting", ex.Message);
        await Assert.ThrowsAsync<OrchestrationException>(() => runner.RespondAsync("nope", new OrchestrationResponse(true)));
    }

    [Fact]
    public async Task Asking_for_changes_without_saying_what_is_refused_and_leaves_the_run_waiting()
    {
        Team();
        var runner = Runner();
        var waiting = await runner.StartAsync(Spec());

        await Assert.ThrowsAsync<OrchestrationException>(() => runner.RespondAsync(waiting.Id, new OrchestrationResponse(false, "  ")));

        Assert.Equal(OrchestrationStatus.WaitingForInput, Stored(waiting.Id)!.Status);
    }

    [Fact]
    public async Task A_waiting_run_can_be_cancelled_and_then_cannot_be_answered()
    {
        Team();
        var runner = Runner();
        var waiting = await runner.StartAsync(Spec());

        var cancelled = runner.Cancel(waiting.Id);

        Assert.Equal(OrchestrationStatus.Cancelled, cancelled!.Status);
        Assert.Null(cancelled.Pending);
        await Assert.ThrowsAsync<OrchestrationException>(() => runner.RespondAsync(waiting.Id, new OrchestrationResponse(true)));
    }

    [Fact]
    public async Task An_interrupted_run_resumes_from_its_checkpoint_and_asks_again()
    {
        Team();
        var options = Options();
        var waiting = await Runner(options).StartAsync(Spec());

        // Simulate a crash mid-run: the record says running, the checkpoint is on disk.
        var store = new OrchestrationStore(options.DatabasePath);
        var record = store.Get(waiting.Id)!;
        record.Status = OrchestrationStatus.Running;
        store.Save(record);
        Assert.Equal(1, store.MarkInterrupted());

        var resumed = await Runner(options).ResumeAsync(waiting.Id);

        Assert.Equal(OrchestrationStatus.WaitingForInput, resumed.Status);
        Assert.Equal(waiting.Pending!.RequestId, resumed.Pending!.RequestId);
    }

    [Fact]
    public async Task Only_an_interrupted_run_can_be_resumed_that_way()
    {
        Team();
        var runner = Runner();
        var waiting = await runner.StartAsync(Spec());

        await Assert.ThrowsAsync<OrchestrationException>(() => runner.ResumeAsync(waiting.Id));
    }

    [Fact]
    public async Task A_failing_agent_ends_the_run_as_failed_with_the_reason()
    {
        Agents["manager"] = new ChatClientAgent(new ThrowingChatClient(), instructions: "m", name: "manager");
        AddAgent("researcher", "x");
        AddAgent("writer", "y");

        var run = await Runner().StartAsync(Spec(signoff: false));

        Assert.Equal(OrchestrationStatus.Failed, run.Status);
        Assert.False(string.IsNullOrWhiteSpace(run.Error));
    }

    private sealed class ThrowingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the model is down");

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the model is down");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}

/// <summary>
/// A model that hands the conversation to a named agent the first time it is offered a
/// handoff tool for it, and answers in plain text after that.
/// </summary>
public sealed class HandingOffChatClient(string target, string afterwards) : IChatClient
{
    private bool _handedOff;
    public List<string> ToolsSeen { get; } = [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var offered = options?.Tools ?? [];
        ToolsSeen.AddRange(offered.Select(t => $"{t.Name} ({t.Description})"));

        // The framework names handoff tools by index and puts the target in the description.
        var handoff = offered.FirstOrDefault(t => t.Name.Contains("handoff", StringComparison.OrdinalIgnoreCase)
                                                  && (t.Description ?? "").Contains(target, StringComparison.OrdinalIgnoreCase))?.Name;
        if (!_handedOff && handoff is not null)
        {
            _handedOff = true;
            var call = new FunctionCallContent("call-1", handoff, new Dictionary<string, object?>());
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call])));
        }

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, afterwards)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var message in response.Messages)
            yield return new ChatResponseUpdate(message.Role, message.Contents);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

public class HandoffOrchestrationTests : OrchestrationTestBase
{
    [Fact]
    public async Task The_lead_hands_the_conversation_to_a_specialist_who_answers()
    {
        var lead = new HandingOffChatClient("billing", "unused");
        Agents["triage"] = new ChatClientAgent(lead, instructions: "Route the request.", name: "triage");
        AddAgent("billing", "Your invoice is paid.");

        var run = await Runner().StartAsync(new OrchestrationSpec(
            OrchestrationKind.Handoff, "Is my invoice paid?", ["triage", "billing"], Lead: "triage"));

        Assert.Equal(OrchestrationStatus.Completed, run.Status);
        Assert.Contains(lead.ToolsSeen, n => n.Contains("billing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(run.Transcript, t => t.Agent == "billing" && t.Text.Contains("paid"));
        Assert.Contains("paid", run.Result);
    }
}

public class OrchestrationValidationTests : OrchestrationTestBase
{
    private async Task<OrchestrationException> Rejected(OrchestrationSpec spec) =>
        await Assert.ThrowsAsync<OrchestrationException>(() => Runner().StartAsync(spec));

    [Fact]
    public async Task A_task_is_required()
    {
        AddAgent("a", "x");
        AddAgent("b", "x");

        var ex = await Rejected(new OrchestrationSpec(OrchestrationKind.GroupChat, " ", ["a", "b"]));

        Assert.Contains("work on", ex.Message);
    }

    [Fact]
    public async Task A_group_chat_and_a_handoff_need_two_agents()
    {
        AddAgent("a", "x");

        Assert.Contains("two", (await Rejected(new OrchestrationSpec(OrchestrationKind.GroupChat, "t", ["a"]))).Message);
        Assert.Contains("two", (await Rejected(new OrchestrationSpec(OrchestrationKind.Handoff, "t", ["a", "A"]))).Message);
    }

    [Fact]
    public async Task An_unknown_agent_is_named_in_the_error()
    {
        AddAgent("a", "x");

        var ex = await Rejected(new OrchestrationSpec(OrchestrationKind.GroupChat, "t", ["a", "ghost"]));

        Assert.Contains("ghost", ex.Message);
    }

    [Fact]
    public async Task Too_many_agents_are_refused()
    {
        for (var i = 0; i < 4; i++) AddAgent($"a{i}", "x");
        var runner = Runner(Options(o => o.MaxAgents = 3));

        var ex = await Assert.ThrowsAsync<OrchestrationException>(() =>
            runner.StartAsync(new OrchestrationSpec(OrchestrationKind.GroupChat, "t", ["a0", "a1", "a2", "a3"])));

        Assert.Contains("limit is 3", ex.Message);
    }

    [Fact]
    public async Task Rounds_are_held_to_the_configured_cap()
    {
        AddAgent("a", "x");
        AddAgent("b", "y");

        var run = await Runner(Options(o => o.MaxRoundsCap = 2)).StartAsync(
            new OrchestrationSpec(OrchestrationKind.GroupChat, "t", ["a", "b"], MaxRounds: 500));

        Assert.Equal(2, run.Spec.MaxRounds);
        Assert.Equal(2, run.Transcript.Count);
    }

    [Fact]
    public async Task A_handoff_lead_is_added_to_the_team_when_left_out()
    {
        AddAgent("triage", "I can answer that directly.");
        AddAgent("billing", "x");

        var run = await Runner().StartAsync(new OrchestrationSpec(OrchestrationKind.Handoff, "Question.", ["billing"], Lead: "triage"));

        Assert.Equal(["triage", "billing"], run.Spec.Agents);
        Assert.Equal(OrchestrationStatus.Completed, run.Status);
        Assert.Contains("directly", run.Result);
    }
}

public class OrchestrationLedgerAndStoreTests : OrchestrationTestBase
{
    [Fact]
    public async Task A_run_is_recorded_in_the_run_ledger_with_its_own_source()
    {
        AddAgent("a", "one");
        AddAgent("b", "two");
        var ledger = new SqliteRunLedger(Path.Combine(Root, "ledger.db"), 500);

        await Runner(ledger: ledger).StartAsync(new OrchestrationSpec(OrchestrationKind.GroupChat, "Talk.", ["a", "b"], MaxRounds: 2));

        var recorded = Assert.Single(ledger.Recent(new RunQuery(Limit: 10)));
        Assert.Equal(RunSource.Orchestration, recorded.Source);
        Assert.Equal(RunStatus.Succeeded, recorded.Status);
        Assert.Contains("Talk.", recorded.Input);
    }

    [Fact]
    public async Task Finished_runs_and_their_checkpoints_are_pruned_after_the_retention_period()
    {
        AddAgent("a", "one");
        AddAgent("b", "two");
        var options = Options();
        var runner = Runner(options);
        var run = await runner.StartAsync(new OrchestrationSpec(OrchestrationKind.GroupChat, "Talk.", ["a", "b"], MaxRounds: 2));
        Assert.True(Directory.Exists(runner.CheckpointPath(run.Id)));

        Assert.Equal(0, runner.PruneOld());

        AgeAllRuns(options.DatabasePath, 40);

        Assert.Equal(1, runner.PruneOld());
        Assert.Null(Stored(run.Id));
        Assert.False(Directory.Exists(runner.CheckpointPath(run.Id)));
    }

    [Fact]
    public async Task A_run_still_waiting_is_not_pruned_however_old()
    {
        AddAgent("manager", "Facts.", "Plan.");
        AddAgent("worker", "x");
        var options = Options();
        var runner = Runner(options);
        var waiting = await runner.StartAsync(new OrchestrationSpec(OrchestrationKind.Magentic, "Do it.", ["worker"], Lead: "manager"));
        Assert.Equal(OrchestrationStatus.WaitingForInput, waiting.Status);

        AgeAllRuns(options.DatabasePath, 400);

        Assert.Equal(0, runner.PruneOld());
        Assert.NotNull(Stored(waiting.Id));
    }
}
