using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Orchestration.Services;

/// <summary>
/// Exit conditions for a single agent run. A run that can only end by the model
/// deciding to stop has no exit condition at all, so every one of these is on by
/// default and each is independent of the others.
/// </summary>
public sealed class LoopGuardOptions
{
    public const string SectionName = "Agent:LoopGuard";

    public bool Enabled { get; set; } = true;

    /// <summary>Hard cap on tool rounds in one run. Reached, the run must answer with what it has.</summary>
    public int MaxToolRounds { get; set; } = 25;

    /// <summary>Token ceiling for a single run, counted across the accumulated prompt.</summary>
    public int MaxRunTokens { get; set; } = 250_000;

    /// <summary>Identical tool calls tolerated before the run is nudged off the repeat.</summary>
    public int RepeatWarningThreshold { get; set; } = 2;

    /// <summary>Identical tool calls tolerated before the run is stopped outright.</summary>
    public int RepeatTerminationThreshold { get; set; } = 4;

    /// <summary>Consecutive identical tool results that count as making no progress.</summary>
    public int StagnationWindow { get; set; } = 3;
}

/// <summary>
/// Why a run stopped. Only <see cref="GoalReached"/> is a success; every other value
/// means the run was cut short and the model is being asked to report what it has.
/// </summary>
public enum RunTermination
{
    GoalReached,
    MaxToolRounds,
    TokenBudget,
    RepeatedAction,
    NoProgress,
}

/// <summary>
/// Applies the run's exit conditions before each model call. When one trips it
/// withdraws the tools and tells the model to close out, which is what actually
/// stops the loop: with no tools in the request the next response is a final
/// message. The alternative, letting the model decide when to stop, is how agents
/// oscillate between two failing approaches until someone kills the process.
/// </summary>
public sealed class LoopGuardChatClient : DelegatingChatClient
{
    private readonly LoopGuardOptions _options;
    private readonly ITokenCounter _counter;
    private readonly ILogger? _logger;

    public LoopGuardChatClient(
        IChatClient inner,
        LoopGuardOptions options,
        ITokenCounter counter,
        ILogger? logger = null) : base(inner)
    {
        _options = options;
        _counter = counter;
        _logger = logger;
    }

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var guarded = Apply(messages.ToList(), options);
        return base.GetResponseAsync(guarded, options, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var guarded = Apply(messages.ToList(), options);
        await foreach (var update in base.GetStreamingResponseAsync(guarded, options, cancellationToken))
            yield return update;
    }

    private List<ChatMessage> Apply(List<ChatMessage> messages, ChatOptions? options)
    {
        if (!_options.Enabled)
            return messages;

        var run = messages.Skip(FindRunStart(messages)).ToList();
        var toolRounds = run.Count(m => m.Role == ChatRole.Assistant && m.Contents.OfType<FunctionCallContent>().Any());

        if (toolRounds == 0)
            return messages;

        var termination = Evaluate(run, toolRounds);
        if (termination is not null)
        {
            AgentTelemetry.RunTerminations.Add(1, new KeyValuePair<string, object?>("reason", termination.Value.ToString()));
            AgentTelemetry.RunToolRounds.Record(toolRounds);

            _logger?.LogWarning(
                "Loop guard stopping run after {Rounds} tool rounds. Reason={Reason}",
                toolRounds, termination.Value);

            Terminate(options);
            return Append(messages, TerminationPrompt(termination.Value, toolRounds));
        }

        var repeated = MostRepeatedCall(run);
        if (repeated is not null && repeated.Value.Count >= _options.RepeatWarningThreshold)
        {
            _logger?.LogInformation(
                "Loop guard: tool '{Tool}' called with identical arguments {Count} times, nudging a different approach",
                repeated.Value.Tool, repeated.Value.Count);

            return Append(messages,
                $"You have already called {repeated.Value.Tool} with these exact arguments "
                + $"{repeated.Value.Count} times and the result has not changed. Do not call it again. "
                + "Either use what you already have, try a materially different approach, or explain "
                + "what you need from the user to continue.");
        }

        return messages;
    }

    private RunTermination? Evaluate(List<ChatMessage> run, int toolRounds)
    {
        if (toolRounds >= _options.MaxToolRounds)
            return RunTermination.MaxToolRounds;

        if (_counter.CountMessages(run) >= _options.MaxRunTokens)
            return RunTermination.TokenBudget;

        var repeated = MostRepeatedCall(run);
        if (repeated is not null && repeated.Value.Count >= _options.RepeatTerminationThreshold)
            return RunTermination.RepeatedAction;

        if (IsStagnant(run))
            return RunTermination.NoProgress;

        return null;
    }

    /// <summary>
    /// The run starts at the last user message that is not a tool result, which is
    /// where the current goal was stated.
    /// </summary>
    private static int FindRunStart(List<ChatMessage> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == ChatRole.User
                && !messages[i].Contents.OfType<FunctionResultContent>().Any())
            {
                return i;
            }
        }
        return 0;
    }

    private static (string Tool, int Count)? MostRepeatedCall(List<ChatMessage> run)
    {
        var counts = new Dictionary<string, (string Tool, int Count)>(StringComparer.Ordinal);

        foreach (var call in run.SelectMany(m => m.Contents.OfType<FunctionCallContent>()))
        {
            var hash = HashCall(call);
            counts[hash] = counts.TryGetValue(hash, out var existing)
                ? (call.Name, existing.Count + 1)
                : (call.Name, 1);
        }

        if (counts.Count == 0)
            return null;

        var top = counts.Values.OrderByDescending(v => v.Count).First();
        return top.Count > 1 ? top : null;
    }

    private static string HashCall(FunctionCallContent call)
    {
        var payload = new StringBuilder(call.Name);
        if (call.Arguments is not null)
        {
            foreach (var argument in call.Arguments.OrderBy(a => a.Key, StringComparer.Ordinal))
                payload.Append('|').Append(argument.Key).Append('=').Append(argument.Value);
        }

        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(payload.ToString())));
    }

    /// <summary>
    /// The state has not moved when the last few tool results are byte-identical.
    /// The guide's other progress signals (metric plateau, shrinking diffs) need a
    /// task-specific notion of progress; identical results need none.
    /// </summary>
    private bool IsStagnant(List<ChatMessage> run)
    {
        var results = run
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .Select(r => r.Result?.ToString() ?? string.Empty)
            .Where(r => r.Length > 0)
            .ToList();

        if (results.Count < _options.StagnationWindow)
            return false;

        var window = results.TakeLast(_options.StagnationWindow).ToList();
        return window.All(r => string.Equals(r, window[0], StringComparison.Ordinal));
    }

    private static void Terminate(ChatOptions? options)
    {
        if (options is null) return;
        options.Tools = null;
        options.ToolMode = ChatToolMode.None;
    }

    private static string TerminationPrompt(RunTermination reason, int toolRounds) => reason switch
    {
        RunTermination.MaxToolRounds =>
            $"You have used the maximum of {toolRounds} tool rounds for this task. Tools are now "
            + "unavailable. Answer with what you have: state what you established, what you could "
            + "not complete, and what the user would need to provide for you to finish.",
        RunTermination.TokenBudget =>
            "This run has reached its token budget. Tools are now unavailable. Summarize what you "
            + "established, what remains, and what you would do next.",
        RunTermination.RepeatedAction =>
            "You have repeated the same tool call with the same arguments several times without a "
            + "new result. Tools are now unavailable. Report what you found, why the approach did "
            + "not work, and what you would try instead.",
        RunTermination.NoProgress =>
            "The last few tool calls returned identical results, so the task is not progressing. "
            + "Tools are now unavailable. Report what you have and what is blocking you.",
        _ => "Answer with the information you already have.",
    };

    private static List<ChatMessage> Append(List<ChatMessage> messages, string instruction)
    {
        var result = new List<ChatMessage>(messages) { new(ChatRole.User, instruction) };
        return result;
    }
}
