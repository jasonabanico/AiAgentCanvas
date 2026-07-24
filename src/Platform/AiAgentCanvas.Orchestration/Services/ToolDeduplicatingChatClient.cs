using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Orchestration.Services;

internal sealed class ToolDeduplicatingChatClient : DelegatingChatClient
{
    public ToolDeduplicatingChatClient(IChatClient inner) : base(inner) { }

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetResponseAsync(messages, DeduplicateTools(options), cancellationToken);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetStreamingResponseAsync(messages, DeduplicateTools(options), cancellationToken);
    }

    private static ChatOptions? DeduplicateTools(ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 })
            return options;

        var seen = new HashSet<string>();
        var unique = new List<AITool>();
        foreach (var tool in options.Tools)
        {
            if (seen.Add(tool.Name))
                unique.Add(tool);
        }

        if (unique.Count == options.Tools.Count)
            return options;

        options.Tools = unique;
        return options;
    }
}
