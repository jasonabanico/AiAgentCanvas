#pragma warning disable MEAI001, MAAI001

using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Capabilities.AgentOrchestration;
using AiAgentCanvas.Capabilities.RunLedger;
using Xunit;

namespace AiAgentCanvas.Tests;

public class SequentialOrchestrationTests : OrchestrationTestBase
{
    [Fact]
    public async Task Each_agent_builds_on_the_one_before_and_the_last_answer_is_the_result()
    {
        var researcher = AddAgent("researcher", "Notes: three facts.");
        var writer = AddAgent("writer", "Summary of the three facts.");

        var run = await Runner().StartAsync(new OrchestrationSpec(
            OrchestrationKind.Sequential, "Summarise the topic.", ["researcher", "writer"]));

        Assert.Equal(OrchestrationStatus.Completed, run.Status);
        Assert.Equal(["researcher", "writer"], run.Transcript.Select(t => t.Agent));
        Assert.Equal("Summary of the three facts.", run.Result);

        // The second agent saw the first agent's work, which is what makes it a pipeline.
        Assert.Contains(writer.Calls.Single(), m => m.Text.Contains("Notes: three facts."));
        Assert.Single(researcher.Calls);
    }

    [Fact]
    public async Task A_pipeline_needs_two_agents()
    {
        AddAgent("solo", "x");

        var ex = await Assert.ThrowsAsync<OrchestrationException>(() => Runner().StartAsync(
            new OrchestrationSpec(OrchestrationKind.Sequential, "Task.", ["solo"])));

        Assert.Contains("at least two", ex.Message);
    }
}

public class ConcurrentOrchestrationTests : OrchestrationTestBase
{
    [Fact]
    public async Task Every_agent_answers_the_same_task_and_the_result_carries_each_answer()
    {
        var security = AddAgent("security", "No injection found.");
        var performance = AddAgent("performance", "One slow query.");

        var run = await Runner().StartAsync(new OrchestrationSpec(
            OrchestrationKind.Concurrent, "Review this change.", ["security", "performance"]));

        Assert.Equal(OrchestrationStatus.Completed, run.Status);
        Assert.Single(security.Calls);
        Assert.Single(performance.Calls);
        Assert.Contains("Review this change.", security.Calls[0].Last().Text);
        Assert.Contains("Review this change.", performance.Calls[0].Last().Text);

        Assert.Contains("security:", run.Result);
        Assert.Contains("No injection found.", run.Result);
        Assert.Contains("performance:", run.Result);
        Assert.Contains("One slow query.", run.Result);
    }

    [Fact]
    public async Task The_agents_do_not_see_each_others_answers()
    {
        var a = AddAgent("a", "Answer from a.");
        var b = AddAgent("b", "Answer from b.");

        await Runner().StartAsync(new OrchestrationSpec(OrchestrationKind.Concurrent, "Task.", ["a", "b"]));

        Assert.DoesNotContain(a.Calls.Single(), m => m.Text.Contains("Answer from b."));
        Assert.DoesNotContain(b.Calls.Single(), m => m.Text.Contains("Answer from a."));
    }
}

public class ReviewOrchestrationTests : OrchestrationTestBase
{
    private const string Approve = """{"approved": true, "feedback": ""}""";
    private static string Revise(string feedback) => $$"""{"approved": false, "feedback": "{{feedback}}"}""";

    private static OrchestrationSpec Spec(int? rounds = null) =>
        new(OrchestrationKind.Review, "Write a slogan.", ["maker", "checker"], MaxRounds: rounds);

    [Fact]
    public async Task The_loop_ends_when_the_checker_approves()
    {
        AddAgent("maker", "Draft one.", "Draft two.");
        AddAgent("checker", Revise("Make it shorter."), Approve);

        var run = await Runner().StartAsync(Spec());

        Assert.Equal(OrchestrationStatus.Completed, run.Status);
        Assert.Equal(ReviewTermination.Approved, run.Termination);
        Assert.Equal("Draft two.", run.Result);
        Assert.Equal(["maker", "checker", "maker", "checker"], run.Transcript.Select(t => t.Agent));
        Assert.Equal("Needs changes: Make it shorter.", run.Transcript[1].Text);
        Assert.Equal("Approved.", run.Transcript[3].Text);
    }

    [Fact]
    public async Task The_maker_receives_its_previous_draft_and_the_feedback()
    {
        var maker = AddAgent("maker", "Draft one.", "Draft two.");
        AddAgent("checker", Revise("Make it shorter."), Approve);

        await Runner().StartAsync(Spec());

        var second = maker.Calls[1].Last().Text;
        Assert.Contains("Write a slogan.", second);
        Assert.Contains("Draft one.", second);
        Assert.Contains("Make it shorter.", second);
    }

    [Fact]
    public async Task The_checker_sees_the_task_and_the_draft_and_is_told_the_reply_format()
    {
        AddAgent("maker", "Draft one.");
        var checker = AddAgent("checker", Approve);

        await Runner().StartAsync(Spec());

        var prompt = checker.Calls.Single().Last().Text;
        Assert.Contains("Write a slogan.", prompt);
        Assert.Contains("Draft one.", prompt);
        Assert.Contains("\"approved\"", prompt);
    }

    [Fact]
    public async Task The_draft_limit_stops_a_checker_that_never_approves_and_the_last_draft_is_the_result()
    {
        var maker = AddAgent("maker", "Draft one.", "Draft two.", "Draft three.", "Draft four.");
        AddAgent("checker", Revise("More."));

        var run = await Runner().StartAsync(Spec(rounds: 3));

        Assert.Equal(OrchestrationStatus.Completed, run.Status);
        Assert.Equal(ReviewTermination.MaxRounds, run.Termination);
        Assert.Equal("Draft three.", run.Result);
        Assert.Equal(3, maker.Calls.Count);
    }

    [Fact]
    public async Task A_revision_that_changes_nothing_ends_the_loop()
    {
        var maker = AddAgent("maker", "Same draft.", "same draft.");
        var checker = AddAgent("checker", Revise("Improve it."));

        var run = await Runner().StartAsync(Spec(rounds: 5));

        Assert.Equal(ReviewTermination.NoProgress, run.Termination);
        Assert.Equal("Same draft.", run.Result);
        Assert.Equal(2, maker.Calls.Count);
        Assert.Single(checker.Calls);
    }

    [Theory]
    [InlineData("This is fine, I guess.")]
    [InlineData("{\"approved\": \"yes\"}")]
    [InlineData("NOT APPROVED, the second line is weak.")]
    public async Task A_reply_the_loop_cannot_read_does_not_count_as_approval(string reply)
    {
        AddAgent("maker", "Draft one.", "Draft two.");
        AddAgent("checker", reply, Approve);

        var run = await Runner().StartAsync(Spec());

        Assert.Equal(ReviewTermination.Approved, run.Termination);
        Assert.Equal("Draft two.", run.Result);
        Assert.StartsWith("Needs changes: ", run.Transcript[1].Text);
    }

    [Fact]
    public async Task A_plain_APPROVED_reply_is_accepted()
    {
        AddAgent("maker", "Draft one.");
        AddAgent("checker", "APPROVED");

        var run = await Runner().StartAsync(Spec());

        Assert.Equal(ReviewTermination.Approved, run.Termination);
    }

    [Fact]
    public async Task A_reply_wrapped_in_prose_and_a_code_fence_is_still_read()
    {
        AddAgent("maker", "Draft one.");
        AddAgent("checker", "Here is my verdict:\n```json\n{\"approved\": true, \"feedback\": \"\"}\n```");

        var run = await Runner().StartAsync(Spec());

        Assert.Equal(ReviewTermination.Approved, run.Termination);
    }

    [Fact]
    public async Task A_review_needs_exactly_two_different_agents()
    {
        AddAgent("maker", "x");
        AddAgent("checker", "x");
        AddAgent("third", "x");
        var runner = Runner();

        await Assert.ThrowsAsync<OrchestrationException>(() => runner.StartAsync(
            new OrchestrationSpec(OrchestrationKind.Review, "Task.", ["maker"])));
        await Assert.ThrowsAsync<OrchestrationException>(() => runner.StartAsync(
            new OrchestrationSpec(OrchestrationKind.Review, "Task.", ["maker", "maker"])));
        var ex = await Assert.ThrowsAsync<OrchestrationException>(() => runner.StartAsync(
            new OrchestrationSpec(OrchestrationKind.Review, "Task.", ["maker", "checker", "third"])));
        Assert.Contains("exactly two", ex.Message);
    }

    [Fact]
    public async Task The_default_draft_limit_comes_from_the_options()
    {
        var maker = AddAgent("maker", "One.", "Two.", "Three.", "Four.", "Five.");
        AddAgent("checker", Revise("More."));

        var run = await Runner(Options(o => o.DefaultReviewRounds = 2)).StartAsync(
            new OrchestrationSpec(OrchestrationKind.Review, "Task.", ["maker", "checker"]));

        Assert.Equal(2, maker.Calls.Count);
        Assert.Equal(2, run.Spec.MaxRounds);
    }

    [Fact]
    public async Task The_outcome_is_stored_with_the_run()
    {
        AddAgent("maker", "Draft one.");
        AddAgent("checker", Approve);

        var run = await Runner().StartAsync(Spec());

        var stored = Stored(run.Id)!;
        Assert.Equal(ReviewTermination.Approved, stored.Termination);
        Assert.Equal("Draft one.", stored.Result);
    }

    [Fact]
    public async Task An_interrupted_review_resumes_from_its_transcript_without_repeating_finished_steps()
    {
        // The process died after the first verdict asked for changes.
        var maker = AddAgent("maker", "Draft two.");
        var checker = AddAgent("checker", Approve);
        var options = Options();
        new OrchestrationStore(options.DatabasePath).Save(new OrchestrationRun
        {
            Id = "interrupted-review",
            Spec = new OrchestrationSpec(OrchestrationKind.Review, "Write a slogan.", ["maker", "checker"], MaxRounds: 3),
            Status = OrchestrationStatus.Interrupted,
            Transcript =
            [
                new TranscriptEntry("maker", "Draft one."),
                new TranscriptEntry("checker", "Needs changes: Make it shorter."),
            ],
        });

        var resumed = await Runner(options).ResumeAsync("interrupted-review");

        Assert.Equal(ReviewTermination.Approved, resumed.Termination);
        Assert.Equal("Draft two.", resumed.Result);
        Assert.Single(maker.Calls);
        Assert.Contains("Make it shorter.", maker.Calls[0].Last().Text);
        Assert.Single(checker.Calls);
    }

    [Fact]
    public async Task A_review_interrupted_before_the_verdict_goes_straight_to_the_checker()
    {
        var maker = AddAgent("maker", "unused");
        AddAgent("checker", Approve);
        var options = Options();
        new OrchestrationStore(options.DatabasePath).Save(new OrchestrationRun
        {
            Id = "no-verdict",
            Spec = new OrchestrationSpec(OrchestrationKind.Review, "Write a slogan.", ["maker", "checker"], MaxRounds: 3),
            Status = OrchestrationStatus.Interrupted,
            Transcript = [new TranscriptEntry("maker", "Draft one.")],
        });

        var resumed = await Runner(options).ResumeAsync("no-verdict");

        Assert.Equal("Draft one.", resumed.Result);
        Assert.Empty(maker.Calls);
    }

    [Fact]
    public async Task A_review_is_recorded_in_the_run_ledger()
    {
        AddAgent("maker", "Draft one.");
        AddAgent("checker", Approve);
        var ledger = new SqliteRunLedger(Path.Combine(Root, "ledger.db"), 500);

        await Runner(ledger: ledger).StartAsync(Spec());

        var recorded = Assert.Single(ledger.Recent(new RunQuery(Limit: 10)));
        Assert.Equal(RunSource.Orchestration, recorded.Source);
        Assert.Contains("Write a slogan.", recorded.Input);
    }
}
