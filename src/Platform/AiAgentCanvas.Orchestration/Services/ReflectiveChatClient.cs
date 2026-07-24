#pragma warning disable MEAI001

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Orchestration.Services;

public sealed class ReflectiveOptions
{
    public int ReflectionInterval { get; set; } = 3;
    public string ReflectionPrompt { get; set; } = DefaultReflectionPrompt;

    internal const string DefaultReflectionPrompt =
        """
        Before continuing, briefly reflect on your progress:
        1. What have you accomplished so far toward the user's goal?
        2. Is your current approach working, or should you adjust?
        3. What is the most important next step?
        Keep your reflection to 2-3 sentences, then proceed.
        """;
}

public sealed class ReflectiveChatClient : DelegatingChatClient
{
    private readonly ReflectiveOptions _options;
    private readonly ILogger? _logger;
    private int _toolRoundsSinceReflection;

    public ReflectiveChatClient(
        IChatClient inner,
        ReflectiveOptions? options = null,
        ILogger? logger = null) : base(inner)
    {
        _options = options ?? new ReflectiveOptions();
        _logger = logger;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messageList = messages.ToList();

        if (ShouldReflect(messageList))
        {
            _logger?.LogInformation("Injecting reflection prompt after {Rounds} tool rounds",
                _toolRoundsSinceReflection);
            messageList = InjectReflection(messageList);
            _toolRoundsSinceReflection = 0;
        }

        var response = await base.GetResponseAsync(messageList, options, cancellationToken);
        TrackToolUsage(response);
        return response;
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messageList = messages.ToList();

        if (ShouldReflect(messageList))
        {
            _logger?.LogInformation("Injecting reflection prompt after {Rounds} tool rounds (streaming)",
                _toolRoundsSinceReflection);
            messageList = InjectReflection(messageList);
            _toolRoundsSinceReflection = 0;
        }

        return base.GetStreamingResponseAsync(messageList, options, cancellationToken);
    }

    private bool ShouldReflect(List<ChatMessage> messages)
    {
        var recentToolRounds = 0;
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            if (msg.Role == ChatRole.Assistant && msg.Contents.OfType<FunctionCallContent>().Any())
                recentToolRounds++;
            else if (msg.Role == ChatRole.User && !msg.Contents.OfType<FunctionResultContent>().Any())
                break;
        }

        _toolRoundsSinceReflection = recentToolRounds;
        return recentToolRounds >= _options.ReflectionInterval;
    }

    private List<ChatMessage> InjectReflection(List<ChatMessage> messages)
    {
        var result = new List<ChatMessage>(messages)
        {
            new(ChatRole.User, _options.ReflectionPrompt)
        };
        return result;
    }

    private void TrackToolUsage(ChatResponse response)
    {
        if (response.Messages.Any(m => m.Contents.OfType<FunctionCallContent>().Any()))
            _toolRoundsSinceReflection++;
    }
}
