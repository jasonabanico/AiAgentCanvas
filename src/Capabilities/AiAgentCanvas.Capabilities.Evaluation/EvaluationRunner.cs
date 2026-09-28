using System.Diagnostics;
using System.Text.Json;
using AiAgentCanvas.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.Evaluation;

/// <summary>
/// Runs an eval case against the built agent, then grades it with a judge model.
/// Two things matter here. The case runs through the agent rather than the raw
/// chat client, so what is measured is the system that ships, tools and context
/// providers included. And the judge is a separate model where one is configured,
/// because a model grading its own output is lenient with it.
/// </summary>
public sealed class EvaluationRunner
{
    // Resolved per run, not injected. The agent is built from every registered tool
    // list, and this capability contributes one of them, so taking AIAgent as a
    // constructor dependency would close a cycle in the container.
    private readonly Func<AIAgent> _agentFactory;
    private readonly IChatClient _judge;
    private readonly bool _judgeIsIndependent;
    private readonly EvaluationStore _store;
    private readonly ILogger<EvaluationRunner> _logger;

    public EvaluationRunner(
        Func<AIAgent> agentFactory,
        IChatClient judge,
        bool judgeIsIndependent,
        EvaluationStore store,
        ILogger<EvaluationRunner> logger)
    {
        _agentFactory = agentFactory;
        _judge = judge;
        _judgeIsIndependent = judgeIsIndependent;
        _store = store;
        _logger = logger;

        if (!_judgeIsIndependent)
        {
            _logger.LogWarning(
                "Evaluation is grading with the same model it is testing. Scores will run high. "
                + "Configure a judge model to separate the two.");
        }
    }

    public async Task<EvalResult> RunAsync(EvalCase evalCase, CancellationToken cancellationToken = default)
    {
        using var activity = AgentTelemetry.Source.StartActivity("eval case", ActivityKind.Internal);
        activity?.SetTag("eval.case", evalCase.Name);
        activity?.SetTag("eval.category", evalCase.Category);

        var sw = Stopwatch.StartNew();

        var (output, toolsUsed) = await RunAgentAsync(evalCase.Input, cancellationToken);
        var (score, passed, rationale) = await JudgeAsync(evalCase, output, toolsUsed, cancellationToken);

        sw.Stop();

        var result = new EvalResult
        {
            EvalCaseId = evalCase.Id,
            EvalCaseName = evalCase.Name,
            ActualOutput = output,
            Score = score,
            Passed = passed,
            Rationale = rationale,
            DurationMs = sw.ElapsedMilliseconds,
            ToolCalls = toolsUsed.Count,
            ToolsUsed = string.Join(", ", toolsUsed.Distinct()),
            ToolUseAccuracy = ScoreToolUse(evalCase, toolsUsed),
            TrajectoryEfficiency = ScoreEfficiency(evalCase, toolsUsed.Count, passed),
            IndependentJudge = _judgeIsIndependent,
        };

        _store.RecordResult(result);

        AgentTelemetry.EvalCases.Add(1,
            new KeyValuePair<string, object?>("category", evalCase.Category),
            new KeyValuePair<string, object?>("outcome", passed ? "pass" : "fail"));

        activity?.SetTag("eval.score", score);
        activity?.SetTag("eval.passed", passed);

        return result;
    }

    private async Task<(string Output, List<string> ToolsUsed)> RunAgentAsync(
        string input, CancellationToken cancellationToken)
    {
        var agent = _agentFactory();
        var session = await agent.CreateSessionAsync(cancellationToken);
        var response = await agent.RunAsync(
            [new ChatMessage(ChatRole.User, input)], session, cancellationToken: cancellationToken);

        var toolsUsed = response.Messages
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .Select(c => c.Name)
            .ToList();

        return (response.Text ?? string.Empty, toolsUsed);
    }

    /// <summary>
    /// Fraction of the case's expected tools that the run actually called. A run
    /// that reached the right answer without touching the tool the case is about
    /// passes the judge and fails this.
    /// </summary>
    private static double? ScoreToolUse(EvalCase evalCase, List<string> toolsUsed)
    {
        if (string.IsNullOrWhiteSpace(evalCase.ExpectedTools))
            return null;

        var expected = evalCase.ExpectedTools
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (expected.Count == 0)
            return null;

        var called = new HashSet<string>(toolsUsed, StringComparer.OrdinalIgnoreCase);
        return (double)expected.Count(called.Contains) / expected.Count;
    }

    private static double? ScoreEfficiency(EvalCase evalCase, int actualSteps, bool passed)
    {
        if (evalCase.OptimalSteps is not { } optimal || optimal <= 0)
            return null;

        if (!passed)
            return 0.0;

        return actualSteps <= 0 ? 1.0 : Math.Min(1.0, (double)optimal / actualSteps);
    }

    private async Task<(double Score, bool Passed, string Rationale)> JudgeAsync(
        EvalCase evalCase, string actualOutput, List<string> toolsUsed, CancellationToken cancellationToken)
    {
        var toolLine = toolsUsed.Count > 0
            ? string.Join(", ", toolsUsed)
            : "(none)";

        var judgePrompt = $$"""
            You are grading an AI agent's response against a rubric. Score strictly and honestly.

            ## Task given to the agent
            {{evalCase.Input}}

            ## Criteria the response must satisfy
            {{evalCase.ExpectedCriteria}}

            ## Tools the agent called
            {{toolLine}}

            ## Agent's actual response
            {{actualOutput}}

            Respond with ONLY a JSON object, no markdown fences, no commentary:
            {"score": <0.0-1.0>, "passed": <true|false>, "rationale": "<one sentence>"}
            """;

        try
        {
            var judgeResponse = await _judge.GetResponseAsync(
                [new ChatMessage(ChatRole.User, judgePrompt)],
                new ChatOptions { Temperature = 0f },
                cancellationToken);

            var json = ExtractJson(judgeResponse.Text ?? "{}");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var score = root.TryGetProperty("score", out var s) ? s.GetDouble() : 0.0;
            var passed = root.TryGetProperty("passed", out var p) && p.GetBoolean();
            var rationale = root.TryGetProperty("rationale", out var r) ? r.GetString() ?? "" : "";
            return (Math.Clamp(score, 0.0, 1.0), passed, rationale);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse judge response for eval case '{EvalCase}'", evalCase.Name);
            return (0.0, false, $"judge_error: {ex.Message}");
        }
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : "{}";
    }
}
