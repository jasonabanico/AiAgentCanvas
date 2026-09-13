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
                [Description("Add an evaluation case: a test input plus the criteria a correct response must satisfy. Category is one of tool_use, planning, memory, output_quality")]
                (string name, string category, string input, string expectedCriteria, string? tags) =>
                {
                    var id = store.AddCase(new EvalCase
                    {
                        Name = name,
                        Category = category,
                        Input = input,
                        ExpectedCriteria = expectedCriteria,
                        Tags = tags,
                    });
                    return JsonSerializer.Serialize(new { id, name, category });
                }, "add_eval_case"),

            AIFunctionFactory.Create(
                [Description("List stored evaluation cases, optionally filtered by category (tool_use, planning, memory, output_quality)")]
                (string? category) =>
                {
                    var cases = store.ListCases(category);
                    return JsonSerializer.Serialize(cases.Select(c => new { c.Id, c.Name, c.Category, c.Tags, c.CreatedAt }));
                }, "list_eval_cases"),

            AIFunctionFactory.Create(
                [Description("Run a single evaluation case by name against the agent's model and grade the response with an LLM judge")]
                async (string name) =>
                {
                    var evalCase = store.GetCaseByName(name);
                    if (evalCase is null)
                        return JsonSerializer.Serialize(new { error = $"No eval case named '{name}'" });

                    var result = await runner.RunAsync(evalCase);
                    return JsonSerializer.Serialize(new
                    {
                        result.EvalCaseName,
                        result.Score,
                        result.Passed,
                        result.Rationale,
                        result.DurationMs,
                    });
                }, "run_eval_case"),

            AIFunctionFactory.Create(
                [Description("Run every evaluation case, optionally filtered by category, and return a pass-rate summary. Use for regression checks after changing a persona, prompt, or tool")]
                async (string? category) =>
                {
                    var cases = store.ListCases(category);
                    var results = new List<EvalResult>();
                    foreach (var evalCase in cases)
                        results.Add(await runner.RunAsync(evalCase));

                    return JsonSerializer.Serialize(new
                    {
                        TotalRun = results.Count,
                        Passed = results.Count(r => r.Passed),
                        AverageScore = results.Count > 0 ? results.Average(r => r.Score) : 0.0,
                        Results = results.Select(r => new { r.EvalCaseName, r.Score, r.Passed }),
                    });
                }, "run_eval_suite"),

            AIFunctionFactory.Create(
                [Description("Get pass-rate and average score statistics from past evaluation runs, optionally filtered by category and time window")]
                (string? category, int? hoursBack) =>
                {
                    var since = hoursBack.HasValue ? DateTimeOffset.UtcNow.AddHours(-hoursBack.Value) : (DateTimeOffset?)null;
                    var stats = store.GetStats(category, since);
                    return JsonSerializer.Serialize(new
                    {
                        stats.TotalRuns,
                        stats.Passed,
                        PassRate = stats.TotalRuns > 0 ? (double)stats.Passed / stats.TotalRuns : 0.0,
                        stats.AverageScore,
                        Period = hoursBack.HasValue ? $"last {hoursBack}h" : "all time",
                    });
                }, "eval_stats"),
        ];
    }
}
