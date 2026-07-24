using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Orchestration.Services;

internal sealed class ToolDeduplicatingChatClient : DelegatingChatClient
{
    private readonly ILogger? _logger;

    public ToolDeduplicatingChatClient(IChatClient inner, ILogger? logger = null) : base(inner)
    {
        _logger = logger;
    }

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

    private ChatOptions? DeduplicateTools(ChatOptions? options)
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

        if (unique.Count != options.Tools.Count)
        {
            _logger?.LogWarning("Deduplicated tools: {OriginalCount} -> {UniqueCount}. Duplicates: {Duplicates}",
                options.Tools.Count, unique.Count,
                string.Join(", ", options.Tools.Select(t => t.Name)
                    .GroupBy(n => n).Where(g => g.Count() > 1)
                    .Select(g => $"{g.Key}(x{g.Count()})")));
            options.Tools = unique;
        }
        else
        {
            _logger?.LogDebug("Tool count for API call: {ToolCount}", unique.Count);
        }

        return options;
    }
}
