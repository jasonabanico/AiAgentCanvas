using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Capabilities.AgentOrchestration;

/// <summary>How a Review run ended. Only <see cref="Approved"/> means the checker accepted the work.</summary>
public static class ReviewTermination
{
    public const string Approved = "Approved";

    /// <summary>The draft limit was reached with the checker still asking for changes. The last draft is the result.</summary>
    public const string MaxRounds = "MaxRounds";

    /// <summary>A revision came back identical to the draft before it, so another round would repeat itself.</summary>
    public const string NoProgress = "NoProgress";
}

/// <summary>
/// The maker-checker loop. A model that grades its own work is lenient with it, so the draft
/// and the verdict come from two different agents. The loop keeps its state in the run's
/// transcript, which is saved after every step, so a restart resumes from the last step that
/// finished and does not pay for the earlier ones again.
/// </summary>
internal static class ReviewLoop
{
    internal const string ApprovedText = "Approved.";
    internal const string RevisePrefix = "Needs changes: ";

    public static async Task RunAsync(
        OrchestrationRun run, AIAgent maker, AIAgent checker, int maxDrafts, Action<OrchestrationRun> save, CancellationToken ct)
    {
        var makerName = run.Spec.Agents[0];
        var checkerName = run.Spec.Agents[1];
        var task = run.Spec.Task;

        // Recover where an earlier pass stopped.
        var draft = run.Transcript.LastOrDefault(t => t.Agent == makerName)?.Text;
        var verdicts = run.Transcript.Count(t => t.Agent == checkerName);
        string? feedback = null;
        var needDraft = true;

        var last = run.Transcript.LastOrDefault();
        if (last is not null && last.Agent == checkerName)
        {
            if (last.Text == ApprovedText)
            {
                Finish(run, draft, ReviewTermination.Approved, save);
                return;
            }

            feedback = last.Text.StartsWith(RevisePrefix, StringComparison.Ordinal) ? last.Text[RevisePrefix.Length..] : last.Text;
        }
        else if (last is not null && last.Agent == makerName)
        {
            needDraft = false;
        }

        while (true)
        {
            if (needDraft)
            {
                if (verdicts >= maxDrafts)
                {
                    Finish(run, draft, ReviewTermination.MaxRounds, save);
                    return;
                }

                var revised = (await AskAsync(maker, MakerPrompt(task, draft, feedback), ct)).Trim();
                if (revised.Length == 0)
                    throw new OrchestrationException($"{makerName} returned an empty draft.");

                if (draft is not null && string.Equals(revised, draft.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    Finish(run, draft, ReviewTermination.NoProgress, save);
                    return;
                }

                draft = revised;
                run.Transcript.Add(new TranscriptEntry(makerName, draft));
                save(run);
            }

            var (approved, notes) = ParseVerdict(await AskAsync(checker, CheckerPrompt(task, draft!), ct));
            verdicts++;
            run.Transcript.Add(new TranscriptEntry(checkerName, approved ? ApprovedText : RevisePrefix + notes));
            save(run);

            if (approved)
            {
                Finish(run, draft, ReviewTermination.Approved, save);
                return;
            }

            feedback = notes;
            needDraft = true;
        }
    }

    private static void Finish(OrchestrationRun run, string? draft, string termination, Action<OrchestrationRun> save)
    {
        run.Termination = termination;
        run.Result = draft;
        run.Pending = null;
        run.Status = OrchestrationStatus.Completed;
        save(run);
    }

    private static async Task<string> AskAsync(AIAgent agent, string prompt, CancellationToken ct)
    {
        var session = await agent.CreateSessionAsync(ct);
        var response = await agent.RunAsync([new ChatMessage(ChatRole.User, prompt)], session, cancellationToken: ct);
        return response.Text ?? string.Empty;
    }

    internal static string MakerPrompt(string task, string? draft, string? feedback) =>
        draft is null
            ? task
            : $"""
              {task}

              Your previous draft:
              {draft}

              The reviewer asked for these changes:
              {(string.IsNullOrWhiteSpace(feedback) ? "Improve the draft." : feedback)}

              Write the full revised draft. Reply with the draft only.
              """;

    internal static string CheckerPrompt(string task, string draft) =>
        $$"""
          You are the reviewer. Judge the draft against the task. Approve only when it fully meets the task. When it does not, say what to change.

          Task:
          {{task}}

          Draft:
          {{draft}}

          Reply with JSON only: {"approved": true or false, "feedback": "what to change, or an empty string when approved"}
          """;

    /// <summary>
    /// Reads the checker's reply. A reply that cannot be understood counts as not approved,
    /// because a loop that treats noise as a pass has no check in it.
    /// </summary>
    internal static (bool Approved, string Feedback) ParseVerdict(string reply)
    {
        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            try
            {
                using var doc = JsonDocument.Parse(reply[start..(end + 1)]);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("approved", out var flag)
                    && flag.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    var feedback = doc.RootElement.TryGetProperty("feedback", out var f) && f.ValueKind == JsonValueKind.String
                        ? f.GetString() ?? string.Empty
                        : string.Empty;
                    return (flag.GetBoolean(), feedback.Trim());
                }
            }
            catch (JsonException)
            {
                // Fall through to the plain-text reading.
            }
        }

        var text = reply.Trim();
        if (text.StartsWith("APPROVED", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("NOT APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            return (true, string.Empty);
        }

        return (false, text.Length == 0 ? "The reviewer gave no detail. Improve the draft." : text);
    }
}
