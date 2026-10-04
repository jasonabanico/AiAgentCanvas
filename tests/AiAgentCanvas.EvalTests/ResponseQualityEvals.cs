using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Xunit;
using Xunit.Abstractions;

namespace AiAgentCanvas.EvalTests;

/// <summary>
/// Response quality cases, graded by the first-party evaluators rather than a
/// hand-written judge prompt. Each case is a scenario in the generated report.
/// </summary>
public class ResponseQualityEvals
{
    private readonly ITestOutputHelper _output;

    public ResponseQualityEvals(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Evaluators are chosen per case rather than applied as a blanket set.
    /// Fluency rewards natural prose, so grading a deliberately terse numbered
    /// list with it fails a response that did exactly what was asked. An
    /// evaluator whose notion of quality contradicts the case produces a false
    /// failure, and false failures are how a suite gets ignored.
    /// </summary>
    public const string Prose = "prose";

    public const string Structured = "structured";

    private static IEvaluator[] EvaluatorsFor(string profile) => profile switch
    {
        Structured => [new RelevanceEvaluator(), new CoherenceEvaluator()],
        _ => [new RelevanceEvaluator(), new CoherenceEvaluator(), new FluencyEvaluator()],
    };

    public static TheoryData<string, string, string> Cases() => new()
    {
        { "explains-a-concept", "Explain in two sentences what retrieval-augmented generation is.", Prose },
        { "answers-a-comparison", "In three sentences, compare vector search with keyword search for retrieval.", Prose },
        { "refuses-out-of-scope", "What is my bank account balance?", Prose },
        { "follows-format", "List exactly three risks of autonomous agents, as a numbered list, no preamble.", Structured },
    };

    [ModelTheory]
    [MemberData(nameof(Cases))]
    public async Task Response_meets_the_quality_bar(string scenario, string prompt, string profile)
    {
        var reporting = EvalHarness.CreateReporting(EvaluatorsFor(profile), nameof(ResponseQualityEvals));
        await using var run = await reporting.CreateScenarioRunAsync(scenario);

        var chatClient = EvalHarness.CreateChatClient();
        var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };
        var response = await chatClient.GetResponseAsync(messages);

        var result = await run.EvaluateAsync(messages, response);

        Report(scenario, result);

        // A diagnostic means the evaluator itself could not run, which is a
        // different failure from the response scoring badly.
        Assert.False(
            result.ContainsDiagnostics(d => d.Severity == EvaluationDiagnosticSeverity.Error),
            $"{scenario}: an evaluator reported an error. See the report for detail.");

        var failed = result.Metrics.Values.Where(m => m.Interpretation?.Failed == true).ToList();
        Assert.True(
            failed.Count == 0,
            $"{scenario}: {string.Join(", ", failed.Select(m => $"{m.Name} ({m.Interpretation?.Reason})"))}");
    }

    private void Report(string scenario, EvaluationResult result)
    {
        foreach (var metric in result.Metrics.Values)
            _output.WriteLine($"{scenario} | {metric.Name}: {Describe(metric)} | {metric.Interpretation?.Rating}");
    }

    private static string Describe(EvaluationMetric metric) => metric switch
    {
        NumericMetric n => n.Value?.ToString("F1") ?? "unset",
        BooleanMetric b => b.Value?.ToString() ?? "unset",
        StringMetric s => s.Value ?? "unset",
        _ => metric.GetType().Name,
    };
}
