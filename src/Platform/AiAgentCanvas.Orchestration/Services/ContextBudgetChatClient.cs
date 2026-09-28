using System.Runtime.CompilerServices;
using System.Text;
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Orchestration.Services;

/// <summary>
/// Token budget for a single prompt, expressed as fractions of the model's context
/// window. The fractions are ceilings, not reservations: a component under its
/// ceiling leaves room for the others.
/// </summary>
public sealed class ContextBudgetOptions
{
    public const string SectionName = "Agent:ContextBudget";

    public bool Enabled { get; set; } = true;

    /// <summary>Context window of the deployed model, in tokens.</summary>
    public int MaxContextTokens { get; set; } = 128_000;

    /// <summary>Tokens held back for the response and never filled with input.</summary>
    public int ReservedOutputTokens { get; set; } = 16_384;

    /// <summary>Ceiling on system instructions, including everything context providers append.</summary>
    public double InstructionsFraction { get; set; } = 0.30;

    /// <summary>Ceiling on tool definitions.</summary>
    public double ToolsFraction { get; set; } = 0.10;

    /// <summary>Ceiling on conversation history.</summary>
    public double HistoryFraction { get; set; } = 0.50;

    /// <summary>Messages at the end of the conversation always kept verbatim.</summary>
    public int KeepRecentMessages { get; set; } = 8;

    /// <summary>
    /// Summarize the dropped span with an LLM instead of discarding it. Costs one extra
    /// call per compaction, so it is worth it for long sessions and not for short ones.
    /// </summary>
    public bool SummarizeDroppedHistory { get; set; } = true;

    /// <summary>Token ceiling on the generated summary.</summary>
    public int SummaryMaxTokens { get; set; } = 512;

    public int InputBudget => Math.Max(1, MaxContextTokens - ReservedOutputTokens);
}

/// <summary>
/// Enforces the prompt budget before every model call: counts what is about to be
/// sent, trims instructions, tool definitions and history to their ceilings, and
/// summarizes the span it drops so the agent does not lose what it already
/// established. Without this the request is truncated by the provider with no
/// error signal.
/// </summary>
public sealed class ContextBudgetChatClient : DelegatingChatClient
{
    private readonly ContextBudgetOptions _options;
    private readonly ITokenCounter _counter;
    private readonly IChatClient _summarizer;
    private readonly ILogger? _logger;

    public ContextBudgetChatClient(
        IChatClient inner,
        ContextBudgetOptions options,
        ITokenCounter counter,
        IChatClient? summarizer = null,
        ILogger? logger = null) : base(inner)
    {
        _options = options;
        _counter = counter;
        _summarizer = summarizer ?? inner;
        _logger = logger;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var trimmed = await EnforceAsync(messages, options, cancellationToken);
        return await base.GetResponseAsync(trimmed, options, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var trimmed = await EnforceAsync(messages, options, cancellationToken);
        await foreach (var update in base.GetStreamingResponseAsync(trimmed, options, cancellationToken))
            yield return update;
    }

    private async Task<List<ChatMessage>> EnforceAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        var history = messages.ToList();

        if (!_options.Enabled)
            return history;

        var instructionTokens = _counter.Count(options?.Instructions);
        var toolTokens = _counter.CountTools(options?.Tools);
        var historyTokens = _counter.CountMessages(history);

        Record("instructions", instructionTokens);
        Record("tools", toolTokens);
        Record("history", historyTokens);

        var budget = _options.InputBudget;
        var instructionCeiling = (int)(budget * _options.InstructionsFraction);
        var toolCeiling = (int)(budget * _options.ToolsFraction);
        var historyCeiling = (int)(budget * _options.HistoryFraction);

        if (options is not null && instructionTokens > instructionCeiling && options.Instructions is not null)
        {
            options.Instructions = TruncateInstructions(options.Instructions, instructionCeiling);
            _logger?.LogWarning(
                "Context budget: instructions trimmed from {Before} tokens to the {Ceiling} token ceiling",
                instructionTokens, instructionCeiling);
            AgentTelemetry.ContextCompactions.Add(1, new KeyValuePair<string, object?>("strategy", "instructions"));
        }

        if (options?.Tools is { Count: > 0 } && toolTokens > toolCeiling)
        {
            var before = options.Tools.Count;
            options.Tools = TrimTools(options.Tools, toolCeiling);
            _logger?.LogWarning(
                "Context budget: tool definitions trimmed from {Before} to {After} tools ({Tokens} tokens over ceiling)",
                before, options.Tools.Count, toolTokens - toolCeiling);
            AgentTelemetry.ContextCompactions.Add(1, new KeyValuePair<string, object?>("strategy", "tools"));
        }

        if (historyTokens > historyCeiling)
        {
            history = await CompactAsync(history, historyCeiling, cancellationToken);
            _logger?.LogInformation(
                "Context budget: history compacted from {Before} to {After} tokens (ceiling {Ceiling})",
                historyTokens, _counter.CountMessages(history), historyCeiling);
        }

        var fixedCost = _counter.Count(options?.Instructions) + _counter.CountTools(options?.Tools);
        if (fixedCost + _counter.CountMessages(history) > budget)
        {
            // Last resort: drop oldest unpinned messages until the prompt fits, rather
            // than handing the provider an oversized request it truncates silently.
            history = HardTrim(history, budget - fixedCost);
            _logger?.LogWarning("Context budget: hard trim applied, prompt exceeded the {Budget} token input budget", budget);
            AgentTelemetry.ContextCompactions.Add(1, new KeyValuePair<string, object?>("strategy", "hard_trim"));
        }

        return history;
    }

    private static void Record(string component, int tokens) =>
        AgentTelemetry.ContextTokens.Record(tokens, new KeyValuePair<string, object?>("component", component));

    private string TruncateInstructions(string instructions, int ceiling)
    {
        // Keep the head: the persona and constraints are written first, and the blocks
        // context providers append (RAG, memory, saved context) are the expendable tail.
        if (_counter.Count(instructions) <= ceiling)
            return instructions;

        var head = instructions.Length > ceiling * 4 ? instructions[..(ceiling * 4)] : instructions;
        while (head.Length > 64 && _counter.Count(head) > ceiling)
            head = head[..(head.Length * 9 / 10)];

        return head + "\n\n[context truncated to fit the prompt budget]";
    }

    private IList<AITool> TrimTools(IList<AITool> tools, int ceiling)
    {
        var kept = new List<AITool>();
        var used = 0;

        foreach (var tool in tools)
        {
            var cost = _counter.CountTools([tool]);
            if (kept.Count > 0 && used + cost > ceiling)
                continue;
            kept.Add(tool);
            used += cost;
        }

        return kept;
    }

    private async Task<List<ChatMessage>> CompactAsync(
        List<ChatMessage> history,
        int ceiling,
        CancellationToken cancellationToken)
    {
        var pinnedCount = CountPinned(history);
        var keepFrom = Math.Max(pinnedCount, history.Count - _options.KeepRecentMessages);

        // Walk the boundary back toward the pinned prefix while the tail still fits,
        // so compaction drops as little as it has to.
        while (keepFrom > pinnedCount && _counter.CountMessages(history.Skip(keepFrom - 1)) <= ceiling)
            keepFrom--;

        keepFrom = AlignToToolBoundary(history, keepFrom);

        if (keepFrom <= pinnedCount)
            return history;

        var result = history.Take(pinnedCount).ToList();
        var dropped = history.Skip(pinnedCount).Take(keepFrom - pinnedCount).ToList();

        if (dropped.Count > 0)
        {
            var summary = _options.SummarizeDroppedHistory
                ? await SummarizeAsync(dropped, cancellationToken)
                : $"[{dropped.Count} earlier messages dropped to fit the prompt budget]";

            result.Add(new ChatMessage(ChatRole.User, summary));
            AgentTelemetry.ContextCompactions.Add(1,
                new KeyValuePair<string, object?>("strategy", _options.SummarizeDroppedHistory ? "summarize" : "drop"));
        }

        result.AddRange(history.Skip(keepFrom));
        return result;
    }

    private async Task<string> SummarizeAsync(List<ChatMessage> dropped, CancellationToken cancellationToken)
    {
        var transcript = new StringBuilder();
        foreach (var message in dropped)
        {
            var calls = message.Contents.OfType<FunctionCallContent>().Select(c => c.Name).ToList();
            if (calls.Count > 0)
                transcript.AppendLine($"{message.Role}: called {string.Join(", ", calls)}");

            var text = message.Text;
            if (!string.IsNullOrWhiteSpace(text))
                transcript.AppendLine($"{message.Role}: {text}");
        }

        var prompt = "Summarize the earlier part of this agent session so the agent can continue "
            + "without re-reading it. Preserve facts established, decisions made, tool results that "
            + "still matter, and approaches that already failed. Drop pleasantries and redundant "
            + "reasoning. Write notes, not prose.\n\n"
            + transcript;

        try
        {
            var response = await _summarizer.GetResponseAsync(
                [new ChatMessage(ChatRole.User, prompt)],
                new ChatOptions { MaxOutputTokens = _options.SummaryMaxTokens, Temperature = 0f },
                cancellationToken);

            var summary = response.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(summary))
                return "## Summary of earlier turns\n" + summary;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "History summarization failed, dropping the span instead");
        }

        return $"[{dropped.Count} earlier messages dropped to fit the prompt budget]";
    }

    private List<ChatMessage> HardTrim(List<ChatMessage> history, int ceiling)
    {
        var pinnedCount = CountPinned(history);
        var result = new List<ChatMessage>(history);

        while (result.Count > pinnedCount + 1 && _counter.CountMessages(result) > ceiling)
        {
            result.RemoveAt(pinnedCount);

            // A dropped assistant turn takes its tool results with it, otherwise the
            // provider rejects the orphaned tool messages that follow.
            while (result.Count > pinnedCount
                && result[pinnedCount].Contents.OfType<FunctionResultContent>().Any())
            {
                result.RemoveAt(pinnedCount);
            }
        }

        return result;
    }

    /// <summary>
    /// The system message and the first user message state the task, so they stay
    /// regardless of budget pressure.
    /// </summary>
    private static int CountPinned(List<ChatMessage> history)
    {
        var pinned = 0;
        while (pinned < history.Count && history[pinned].Role == ChatRole.System)
            pinned++;

        if (pinned < history.Count && history[pinned].Role == ChatRole.User)
            pinned++;

        return pinned;
    }

    /// <summary>Never start the retained tail on a tool result whose call was dropped.</summary>
    private static int AlignToToolBoundary(List<ChatMessage> history, int keepFrom)
    {
        while (keepFrom < history.Count
            && history[keepFrom].Contents.OfType<FunctionResultContent>().Any())
        {
            keepFrom++;
        }
        return keepFrom;
    }
}
