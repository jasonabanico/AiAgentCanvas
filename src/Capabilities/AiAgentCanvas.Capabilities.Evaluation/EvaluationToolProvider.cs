#pragma warning disable MEAI001

using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Capabilities.Evaluation;

public static class EvaluationToolProvider
{
    public static IReadOnlyList<AITool> CreateTools(EvaluationStore store, EvaluationRunner runner)
    {
        return
        [
            AIFunctionFactory.Create(
                [Description("Add an evaluation case: a test input plus the criteria a correct response must satisfy. Category is one of tool_use, planning, memory, output_quality. Supply expectedTools and optimalSteps to measure how the agent got there, not just whether the answer was acceptable")]
                (string name, string category, string input, string expectedCriteria, string? tags, string? expectedTools, int? optimalSteps) =>
                {
                    var id = store.AddCase(new EvalCase
                    {
                        Name = name,
                        Category = category,
                        Input = input,
                        ExpectedCriteria = expectedCriteria,
                        Tags = tags,
                        ExpectedTools = expectedTools,
                        OptimalSteps = optimalSteps,
                    });
                    return JsonSerializer.Serialize(new { id, name, category });
                }, "add_eval_case"),

            AIFunctionFactory.Create(
                [Description("List stored evaluation cases, optionally filtered by category (tool_use, planning, memory, output_quality)")]
                (string? category) =>
                {
                    var cases = store.ListCases(category);
                    return JsonSerializer.Serialize(cases.Select(c => new
                    {
                        c.Id, c.Name, c.Category, c.Tags, c.ExpectedTools, c.OptimalSteps, c.CreatedAt,
                    }));
                }, "list_eval_cases"),

            AIFunctionFactory.Create(
                [Description("Run a single evaluation case by name against the full agent and grade it with the judge model")]
                async (string name, CancellationToken ct) =>
                {
                    var evalCase = store.GetCaseByName(name);
                    if (evalCase is null)
                        return JsonSerializer.Serialize(new { error = $"No eval case named '{name}'" });

                    var result = await runner.RunAsync(evalCase, ct);
                    return JsonSerializer.Serialize(Describe(result));
                }, "run_eval_case"),

            AIFunctionFactory.Create(
                [Description("Run every evaluation case, optionally filtered by category, and return a summary. Use for a regression check after changing a persona, prompt, or tool")]
                async (string? category, CancellationToken ct) =>
                {
                    var cases = store.ListCases(category);
                    var results = new List<EvalResult>();
                    foreach (var evalCase in cases)
                        results.Add(await runner.RunAsync(evalCase, ct));

                    var withToolScore = results.Where(r => r.ToolUseAccuracy.HasValue).ToList();
                    var withEfficiency = results.Where(r => r.TrajectoryEfficiency.HasValue).ToList();

                    return JsonSerializer.Serialize(new
                    {
                        TotalRun = results.Count,
                        Passed = results.Count(r => r.Passed),
                        TaskSuccessRate = results.Count > 0 ? (double)results.Count(r => r.Passed) / results.Count : 0.0,
                        AverageScore = results.Count > 0 ? results.Average(r => r.Score) : 0.0,
                        AverageToolUseAccuracy = withToolScore.Count > 0 ? withToolScore.Average(r => r.ToolUseAccuracy!.Value) : (double?)null,
                        AverageTrajectoryEfficiency = withEfficiency.Count > 0 ? withEfficiency.Average(r => r.TrajectoryEfficiency!.Value) : (double?)null,
                        IndependentJudge = results.All(r => r.IndependentJudge),
                        Results = results.Select(Describe),
                    });
                }, "run_eval_suite"),

            AIFunctionFactory.Create(
                [Description("Get pass rate, average score, tool-use accuracy and trajectory efficiency from past evaluation runs, optionally filtered by category and time window")]
                (string? category, int? hoursBack) =>
                {
                    var since = hoursBack.HasValue ? DateTimeOffset.UtcNow.AddHours(-hoursBack.Value) : (DateTimeOffset?)null;
                    var stats = store.GetStats(category, since);
                    return JsonSerializer.Serialize(new
                    {
                        stats.TotalRuns,
                        stats.Passed,
                        stats.PassRate,
                        stats.AverageScore,
                        stats.AverageToolUseAccuracy,
                        stats.AverageTrajectoryEfficiency,
                        Period = hoursBack.HasValue ? $"last {hoursBack}h" : "all time",
                    });
                }, "eval_stats"),
        ];
    }

    private static object Describe(EvalResult result) => new
    {
        result.EvalCaseName,
        result.Score,
        result.Passed,
        result.Rationale,
        result.DurationMs,
        result.ToolCalls,
        result.ToolsUsed,
        result.ToolUseAccuracy,
        result.TrajectoryEfficiency,
        result.IndependentJudge,
    };
}
