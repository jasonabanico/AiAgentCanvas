#pragma warning disable MEAI001

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Orchestration.Services;

public sealed class ModelRouterOptions
{
    public IChatClient? EconomyClient { get; set; }
    public int ComplexityThresholdTokens { get; set; } = 200;
    public IReadOnlyList<string>? ComplexityKeywords { get; set; }
}

public sealed class CostAwareModelRouter : DelegatingChatClient
{
    private readonly IChatClient? _economyClient;
    private readonly int _complexityThreshold;
    private readonly HashSet<string> _complexityKeywords;
    private readonly ILogger? _logger;

    public CostAwareModelRouter(
        IChatClient primaryClient,
        ModelRouterOptions options,
        ILogger? logger = null) : base(primaryClient)
    {
        _economyClient = options.EconomyClient;
        _complexityThreshold = options.ComplexityThresholdTokens;
        _complexityKeywords = new HashSet<string>(
            options.ComplexityKeywords ?? DefaultComplexityKeywords,
            StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var client = SelectClient(messages, options);
        return await client.GetResponseAsync(messages, options, cancellationToken);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var client = SelectClient(messages, options);
        return client.GetStreamingResponseAsync(messages, options, cancellationToken);
    }

    private IChatClient SelectClient(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        if (_economyClient is null)
            return InnerClient;

        var complexity = ScoreComplexity(messages, options);
        var usePrimary = complexity >= _complexityThreshold;

        _logger?.LogInformation(
            "Model routing: complexity={Complexity}, threshold={Threshold}, route={Route}",
            complexity, _complexityThreshold, usePrimary ? "primary" : "economy");

        return usePrimary ? InnerClient : _economyClient;
    }

    internal int ScoreComplexity(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var score = 0;
        var messageList = messages.ToList();

        var lastUserMessage = messageList
            .LastOrDefault(m => m.Role == ChatRole.User);

        if (lastUserMessage is null)
            return score;

        var text = string.Join(" ", lastUserMessage.Contents
            .OfType<TextContent>()
            .Select(c => c.Text));

        var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        score += wordCount;

        foreach (var keyword in _complexityKeywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                score += 50;
        }

        if (options?.Tools is { Count: > 0 })
            score += options.Tools.Count * 20;

        if (messageList.Count > 10)
            score += (messageList.Count - 10) * 5;

        if (lastUserMessage.Contents.Any(c => c is not TextContent))
            score += 100;

        return score;
    }

    private static readonly string[] DefaultComplexityKeywords =
    [
        "analyze", "compare", "explain", "reason", "evaluate",
        "synthesize", "design", "architect", "debug", "refactor",
        "multi-step", "trade-off", "strategy", "plan", "complex",
    ];
}
