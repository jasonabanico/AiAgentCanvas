using AiAgentCanvas.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.Rag;

/// <summary>
/// Searches on each user message and adds the best passages, numbered for citation, to the
/// agent's instructions. It is the always-on form of retrieval. An agent that should decide
/// for itself when to look something up uses the <c>rag_search</c> tool instead, with
/// <see cref="RagOptions.AutoInject"/> off.
/// </summary>
public sealed class RagContextProvider : AIContextProvider
{
    private readonly RagSearcher _searcher;
    private readonly ILogger<RagContextProvider> _logger;

    public RagContextProvider(RagSearcher searcher, ILogger<RagContextProvider> logger)
    {
        _searcher = searcher;
        _logger = logger;
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken)
    {
        var lastUserMessage = context.AIContext.Messages?
            .LastOrDefault(m => m.Role == ChatRole.User)?.Text;

        if (string.IsNullOrWhiteSpace(lastUserMessage))
            return new AIContext();

        _logger.LogDebug("RAG search for: {Query}", lastUserMessage);

        var results = await _searcher.SearchAsync([lastUserMessage], ct: cancellationToken);
        if (results.Count == 0)
        {
            _logger.LogDebug("No RAG results found");
            return new AIContext();
        }

        _logger.LogInformation("RAG using {Used} passages", results.Count);

        var citations = results.Select((r, i) =>
        {
            var source = r.Record.Source ?? "unknown";
            var score = r.Score?.ToString("F4") ?? "n/a";
            var preview = r.Record.Text.Length > 200 ? r.Record.Text[..200] + "..." : r.Record.Text;
            return $"[{i + 1}] (source: {source}, score: {score})\n{preview}";
        });

        var ragContext = string.Join("\n\n---\n\n", citations);
        // Only the addition: the agent merges it into the instructions it already has.
        return new AIContext
        {
            Instructions = $"""
                Relevant context from documents (cite by number when using):
                {ragContext}
                """,
        };
    }
}
