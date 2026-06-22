using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using AiAgentCanvas.Abstractions;

namespace AiAgentCanvas.Core.Services;

public sealed class LlmReranker
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<LlmReranker> _logger;

    public LlmReranker(IChatClient chatClient, ILogger<LlmReranker> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<List<VectorSearchResult<DocumentRecord>>> RerankAsync(
        string query,
        List<VectorSearchResult<DocumentRecord>> candidates,
        int topN = 3,
        CancellationToken ct = default)
    {
        if (candidates.Count <= topN)
            return candidates;

        var numbered = candidates.Select((c, i) => new { Index = i, Text = Truncate(c.Record.Text, 300) }).ToList();

        var prompt = $"""
            Given the query: "{query}"

            Rank the following document chunks by relevance to the query. Return ONLY a JSON array of the chunk numbers in order from most relevant to least relevant. Example: [2,0,4,1,3]

            {string.Join("\n\n", numbered.Select(n => $"[{n.Index}]: {n.Text}"))}
            """;

        try
        {
            var response = await _chatClient.GetResponseAsync(
                [new ChatMessage(ChatRole.User, prompt)],
                new ChatOptions { MaxOutputTokens = 100, Temperature = 0f },
                ct);

            var text = response.Text?.Trim() ?? "";
            var jsonStart = text.IndexOf('[');
            var jsonEnd = text.LastIndexOf(']');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = text[jsonStart..(jsonEnd + 1)];
                var ranking = JsonSerializer.Deserialize<int[]>(json);

                if (ranking is not null)
                {
                    var reranked = ranking
                        .Where(i => i >= 0 && i < candidates.Count)
                        .Distinct()
                        .Take(topN)
                        .Select(i => candidates[i])
                        .ToList();

                    _logger.LogDebug("Reranked {Count} candidates to {TopN}", candidates.Count, reranked.Count);
                    return reranked;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reranking failed, falling back to original ranking");
        }

        return candidates.Take(topN).ToList();
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
