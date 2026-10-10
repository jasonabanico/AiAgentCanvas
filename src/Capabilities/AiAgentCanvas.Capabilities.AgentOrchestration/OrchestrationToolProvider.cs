using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Capabilities.AgentOrchestration;

public static class OrchestrationToolProvider
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Tools for starting, reading and cancelling runs. There is deliberately no tool to
    /// answer a run that is waiting for a person. An agent that could approve its own plan
    /// would make the sign-off meaningless, so answers come only through the authorized
    /// HTTP endpoint.
    /// </summary>
    public static IReadOnlyList<AITool> CreateTools(OrchestrationRunner runner, OrchestrationStore store)
    {
        return
        [
            AIFunctionFactory.Create(
                [Description("Run several named agents together on one task. kind is GroupChat (agents take turns), Handoff (a lead passes work to specialists and takes it back), Magentic (a manager plans, assigns work and tracks progress), Sequential (a pipeline: each agent builds on the one before), Concurrent (all agents answer the same task at once and the answers are gathered) or Review (two agents: the first drafts, the second checks the draft and asks for changes until it approves, the draft limit is reached or a revision changes nothing). A Magentic run stops for a person to approve its plan unless requireSignoff is false, and returns status WaitingForInput: tell the user the run id and that a person must answer it. Do not try to answer it yourself.")]
                async (
                    [Description("GroupChat, Handoff, Magentic, Sequential, Concurrent or Review")] string kind,
                    [Description("What the agents should work on")] string task,
                    [Description("Names of the agents that take part. Sequential runs them in this order. Review takes exactly two: the maker, then the checker")] string[] agents,
                    [Description("Handoff: the agent that starts. Magentic: the manager. Defaults to the first agent or the default agent")] string? lead = null,
                    [Description("Most turns or rounds. For Review, the most drafts the maker may write")] int? maxRounds = null,
                    [Description("Magentic only. Stop for a person to approve the plan. Defaults to true")] bool requireSignoff = true,
                    CancellationToken ct = default) =>
                {
                    if (!Enum.TryParse<OrchestrationKind>(kind, true, out var parsed))
                        return Error("kind must be GroupChat, Handoff, Magentic, Sequential, Concurrent or Review.");

                    try
                    {
                        var run = await runner.StartAsync(new OrchestrationSpec(parsed, task, agents ?? [], lead, maxRounds, requireSignoff), ct);
                        return Summarize(run);
                    }
                    catch (OrchestrationException ex)
                    {
                        return Error(ex.Message);
                    }
                },
                "start_orchestration"),

            AIFunctionFactory.Create(
                [Description("Get the state of an orchestration run: its status, what it is waiting for, the transcript and the result")]
                (string runId) =>
                {
                    var run = store.Get(runId);
                    return run is null ? Error($"No orchestration run '{runId}'.") : Summarize(run);
                },
                "get_orchestration"),

            AIFunctionFactory.Create(
                [Description("List recent orchestration runs, newest first. status may be Running, WaitingForInput, Completed, Failed, Cancelled or Interrupted")]
                (string? status = null, int? limit = null) =>
                {
                    OrchestrationStatus? filter = Enum.TryParse<OrchestrationStatus>(status, true, out var s) ? s : null;
                    var runs = store.List(limit ?? 20, filter).Select(r => new
                    {
                        r.Id,
                        Kind = r.Spec.Kind,
                        r.Status,
                        Task = Clip(r.Spec.Task, 120),
                        r.UpdatedAt,
                    });
                    return JsonSerializer.Serialize(new { runs }, Json);
                },
                "list_orchestrations"),

            AIFunctionFactory.Create(
                [Description("Cancel an orchestration run that is waiting for a person or was interrupted")]
                (string runId) =>
                {
                    var run = runner.Cancel(runId);
                    return run is null ? Error($"No orchestration run '{runId}'.") : Summarize(run);
                },
                "cancel_orchestration"),
        ];
    }

    internal static string Summarize(OrchestrationRun run) => JsonSerializer.Serialize(new
    {
        run.Id,
        Kind = run.Spec.Kind,
        run.Status,
        run.Pending,
        run.Result,
        run.Termination,
        Transcript = run.Transcript.Select(t => new { t.Agent, Text = Clip(t.Text, 1500) }),
        run.Error,
        note = run.Status == OrchestrationStatus.WaitingForInput
            ? "A person must answer this run. It cannot be answered with a tool."
            : null,
    }, Json);

    private static string Error(string message) => JsonSerializer.Serialize(new { error = message });

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max] + "...";
}
