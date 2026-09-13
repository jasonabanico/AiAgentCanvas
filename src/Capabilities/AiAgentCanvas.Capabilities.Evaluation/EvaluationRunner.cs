using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.Evaluation;

/// <summary>
/// Runs an eval case against the raw chat client and grades the response with an
/// LLM-as-judge pass using the same client. Uses the unwrapped <see cref="IChatClient"/>
/// (not the built agent) so evaluation never depends on the tools/pipeline it is measuring.
/// </summary>
public sealed class EvaluationRunner
{
    private readonly IChatClient _chatClient;
    private readonly EvaluationStore _store;
    private readonly ILogger<EvaluationRunner> _logger;

    public EvaluationRunner(IChatClient chatClient, EvaluationStore store, ILogger<EvaluationRunner> logger)
    {
        _chatClient = chatClient;
        _store = store;
        _logger = logger;
    }

    public async Task<EvalResult> RunAsync(EvalCase evalCase, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        var response = await _chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, evalCase.Input)],
            cancellationToken: cancellationToken);
        var actualOutput = response.Text ?? string.Empty;

        var (score, passed, rationale) = await JudgeAsync(evalCase, actualOutput, cancellationToken);
        sw.Stop();

        var result = new EvalResult
        {
            EvalCaseId = evalCase.Id,
            EvalCaseName = evalCase.Name,
            ActualOutput = actualOutput,
            Score = score,
            Passed = passed,
            Rationale = rationale,
            DurationMs = sw.ElapsedMilliseconds,
        };

        _store.RecordResult(result);
        return result;
    }

    private async Task<(double Score, bool Passed, string Rationale)> JudgeAsync(
        EvalCase evalCase, string actualOutput, CancellationToken cancellationToken)
    {
        var judgePrompt = $$"""
            You are grading an AI agent's response against a rubric. Score strictly and honestly.

            ## Task given to the agent
            {{evalCase.Input}}

            ## Criteria the response must satisfy
            {{evalCase.ExpectedCriteria}}

            ## Agent's actual response
            {{actualOutput}}

            Respond with ONLY a JSON object, no markdown fences, no commentary:
            {"score": <0.0-1.0>, "passed": <true|false>, "rationale": "<one sentence>"}
            """;

        try
        {
            var judgeResponse = await _chatClient.GetResponseAsync(
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
