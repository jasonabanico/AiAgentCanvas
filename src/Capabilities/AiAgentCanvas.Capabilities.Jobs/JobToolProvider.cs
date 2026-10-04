#pragma warning disable MEAI001

using System.ComponentModel;
using System.Text.Json;
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Capabilities.Jobs;

public static class JobToolProvider
{
    public static IReadOnlyList<AITool> CreateTools(IJobRunner runner)
    {
        return
        [
            AIFunctionFactory.Create(
                [Description("List the jobs that can be run: fixed, repeatable tasks that run without a model")]
                () => JsonSerializer.Serialize(runner.List()),
                "list_jobs"),

            AIFunctionFactory.Create(
                [Description("Run a job now and return its result. Prefer a job over doing the same steps by hand when one exists")]
                async (string name, Dictionary<string, string>? arguments, CancellationToken ct) =>
                {
                    var result = await runner.RunAsync(new JobRequest(name, arguments), ct);
                    return JsonSerializer.Serialize(new { result.Ok, result.Summary, result.Data });
                },
                "run_job"),
        ];
    }
}
