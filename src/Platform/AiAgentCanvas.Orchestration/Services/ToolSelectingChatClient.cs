#pragma warning disable MEAI001

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Orchestration.Services;

/// <summary>
/// Limits how many tool definitions one request carries. A host with connectors, connected
/// MCP servers and skills can register hundreds of tools. Putting all of them in every prompt
/// costs tokens on each call and gives the model more near-duplicates to choose between,
/// which lowers the chance it picks the right one.
/// </summary>
public sealed class ToolSelectionOptions
{
    public const string SectionName = "Agent:ToolSelection";

    /// <summary>
    /// Off by default, because hiding a tool the model needed is worse than offering one it
    /// did not. Turn it on for a host with many tools, such as one with several connectors or
    /// connected MCP servers. The <c>context_compactions</c> counter with strategy
    /// <c>tools</c> shows when the prompt budget is already dropping tools by position.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// A request with this many tools or fewer is left alone. Above it, the request keeps the
    /// tools most relevant to the user's message, up to this many.
    /// </summary>
    public int MaxTools { get; set; } = 24;

    /// <summary>Tool names that are always offered. A trailing <c>*</c> matches a family such as <c>system_*</c>.</summary>
    public List<string> AlwaysInclude { get; set; } = [];

    /// <summary>Rank with the embedding model when one is configured, together with the keyword ranking.</summary>
    public bool UseEmbeddings { get; set; } = true;
}

/// <summary>
/// Narrows the tool list to the tools relevant to the current request before the prompt budget
/// is applied. The choice is made from the user's message that started the run, so it is the
/// same on every round of that run, and a tool the run has already called stays in the list.
/// Without this step the budget drops tools by their position in the list, which has nothing
/// to do with the task.
/// </summary>
public sealed class ToolSelectingChatClient : DelegatingChatClient
{
    private readonly ToolSelectionOptions _options;
    private readonly IEmbeddingGenerator<string, Embedding<float>>? _embedder;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, float[]> _toolVectors = new();
    private readonly ConcurrentDictionary<string, float[]> _queryVectors = new();

    public ToolSelectingChatClient(
        IChatClient inner,
        ToolSelectionOptions options,
        IEmbeddingGenerator<string, Embedding<float>>? embedder = null,
        ILogger? logger = null) : base(inner)
    {
        _options = options;
        _embedder = options.UseEmbeddings ? embedder : null;
        _logger = logger;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        return await base.GetResponseAsync(list, await SelectAsync(list, options, cancellationToken), cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        var selected = await SelectAsync(list, options, cancellationToken);
        await foreach (var update in base.GetStreamingResponseAsync(list, selected, cancellationToken))
            yield return update;
    }

    internal async Task<ChatOptions?> SelectAsync(List<ChatMessage> messages, ChatOptions? options, CancellationToken ct)
    {
        if (!_options.Enabled || options?.Tools is not { } tools || tools.Count <= Math.Max(1, _options.MaxTools))
            return options;

        var query = RunQuery(messages);
        if (string.IsNullOrWhiteSpace(query))
            return options;

        var calledInRun = CalledSinceLastUserTurn(messages);
        var required = tools
            .Where(t => calledInRun.Contains(t.Name) || MatchesAlwaysInclude(t.Name))
            .ToList();

        var room = Math.Max(0, Math.Max(1, _options.MaxTools) - required.Count);
        var candidates = tools.Where(t => !required.Contains(t)).ToList();
        var ranked = await RankAsync(query, candidates, ct);
        var chosen = new HashSet<AITool>(required);
        foreach (var tool in ranked.Take(room))
            chosen.Add(tool);

        // Keep the registered order, so the request is the same shape as it would have been.
        var kept = tools.Where(chosen.Contains).ToList();

        _logger?.LogInformation(
            "Tool selection: {Offered} of {Registered} tools offered for this request ({Required} required)",
            kept.Count, tools.Count, required.Count);
        AgentTelemetry.ContextCompactions.Add(1, new KeyValuePair<string, object?>("strategy", "tool_selection"));

        // A copy, because the options object belongs to the agent and the next run needs the full list.
        var copy = options.Clone();
        copy.Tools = kept;
        return copy;
    }

    private bool MatchesAlwaysInclude(string name) =>
        _options.AlwaysInclude.Any(pattern =>
            pattern.EndsWith('*')
                ? name.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
                : string.Equals(pattern, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The user message that started the current run: the last one that is not a tool result.</summary>
    private static string? RunQuery(List<ChatMessage> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == ChatRole.User && !messages[i].Contents.OfType<FunctionResultContent>().Any())
                return messages[i].Text;
        }
        return null;
    }

    private static HashSet<string> CalledSinceLastUserTurn(List<ChatMessage> messages)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var message = messages[i];
            if (message.Role == ChatRole.User && !message.Contents.OfType<FunctionResultContent>().Any())
                break;

            foreach (var call in message.Contents.OfType<FunctionCallContent>())
                names.Add(call.Name);
        }
        return names;
    }

    private async Task<List<AITool>> RankAsync(string query, List<AITool> candidates, CancellationToken ct)
    {
        var byKeyword = KeywordOrder(query, candidates);

        if (_embedder is null)
            return byKeyword;

        try
        {
            var byMeaning = await EmbeddingOrderAsync(query, candidates, ct);
            return Fuse(byKeyword, byMeaning);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Tool ranking by embedding failed, using the keyword ranking");
            return byKeyword;
        }
    }

    private async Task<List<AITool>> EmbeddingOrderAsync(string query, List<AITool> candidates, CancellationToken ct)
    {
        var queryVector = _queryVectors.TryGetValue(query, out var cached)
            ? cached
            : (await _embedder!.GenerateVectorAsync(query, cancellationToken: ct)).ToArray();

        if (_queryVectors.Count > 64)
            _queryVectors.Clear();
        _queryVectors[query] = queryVector;

        var missing = candidates.Where(t => !_toolVectors.ContainsKey(Key(t))).ToList();
        if (missing.Count > 0)
        {
            var generated = await _embedder!.GenerateAsync(missing.Select(Describe).ToList(), cancellationToken: ct);
            for (var i = 0; i < missing.Count; i++)
                _toolVectors[Key(missing[i])] = generated[i].Vector.ToArray();
        }

        return candidates
            .Select(t => (Tool: t, Score: Cosine(queryVector, _toolVectors[Key(t)])))
            .OrderByDescending(x => x.Score)
            .Select(x => x.Tool)
            .ToList();
    }

    private static string Key(AITool tool) => tool.Name + "\0" + tool.Description;

    private static string Describe(AITool tool) => $"{tool.Name.Replace('_', ' ')}: {tool.Description}";

    private static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0;

        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        var magnitude = Math.Sqrt(magA) * Math.Sqrt(magB);
        return magnitude == 0 ? 0 : dot / magnitude;
    }

    private static List<AITool> Fuse(List<AITool> first, List<AITool> second)
    {
        var scores = new Dictionary<AITool, double>();
        foreach (var ranking in new[] { first, second })
        {
            for (var i = 0; i < ranking.Count; i++)
                scores[ranking[i]] = scores.GetValueOrDefault(ranking[i]) + 1.0 / (60 + i + 1);
        }

        // OrderBy is stable, so ties keep the registered order.
        return first.OrderByDescending(t => scores[t]).ToList();
    }

    private static readonly HashSet<string> StopWords =
    [
        "the", "and", "for", "with", "that", "this", "from", "into", "about", "your", "you", "are", "can", "will",
        "use", "when", "what", "how", "has", "have", "its", "any", "all", "get", "please", "need", "want",
    ];

    /// <summary>
    /// Scores each tool by the query words found in its name (worth three) and its description
    /// (worth one), weighted by how rare the word is across the candidates. A tool nothing
    /// matches keeps its place in the registered order.
    /// </summary>
    internal static List<AITool> KeywordOrder(string query, List<AITool> candidates)
    {
        var queryTokens = Tokens(query).Distinct().ToList();
        if (queryTokens.Count == 0)
            return candidates;

        var nameTokens = candidates.Select(t => Tokens(t.Name).ToHashSet()).ToList();
        var descriptionTokens = candidates.Select(t => Tokens(t.Description ?? "").ToHashSet()).ToList();

        double Idf(string token)
        {
            var df = Enumerable.Range(0, candidates.Count).Count(i => nameTokens[i].Contains(token) || descriptionTokens[i].Contains(token));
            return Math.Log(1.0 + candidates.Count / (1.0 + df));
        }

        var weights = queryTokens.ToDictionary(t => t, Idf);

        return Enumerable.Range(0, candidates.Count)
            .Select(i => (Index: i, Score: queryTokens.Sum(t =>
                weights[t] * ((nameTokens[i].Contains(t) ? 3.0 : 0.0) + (descriptionTokens[i].Contains(t) ? 1.0 : 0.0)))))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index)
            .Select(x => candidates[x.Index])
            .ToList();
    }

    private static IEnumerable<string> Tokens(string text)
    {
        foreach (Match match in Regex.Matches(text.ToLowerInvariant(), "[a-z0-9]+"))
        {
            var word = match.Value;
            if (word.Length > 3 && word.EndsWith('s'))
                word = word[..^1];

            if (word.Length >= 3 && !StopWords.Contains(word))
                yield return word;
        }
    }
}
