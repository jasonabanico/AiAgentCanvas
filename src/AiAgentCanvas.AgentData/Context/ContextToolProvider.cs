using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.AgentData.Context;

public sealed class ContextToolProvider
{
    private readonly ContextStore _store;
    private readonly ILogger<ContextToolProvider> _logger;

    public ContextToolProvider(ContextStore store, ILogger<ContextToolProvider> logger)
    {
        _store = store;
        _logger = logger;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(SaveContext, "save_context",
                "Save a piece of persistent context for future conversations"),
            AIFunctionFactory.Create(UpdateContext, "update_context",
                "Update an existing persistent context entry"),
            AIFunctionFactory.Create(ListContext, "list_context",
                "List all persistent context entries"),
            AIFunctionFactory.Create(ReadContext, "read_context",
                "Read the full content of a persistent context entry"),
            AIFunctionFactory.Create(DeleteContext, "delete_context",
                "Delete a persistent context entry"),
        ];
    }

    [Description("Save a piece of persistent context that will be included in future conversations")]
    private string SaveContext(
        [Description("Topic name for this context (e.g. 'project-goals', 'team-preferences')")] string topic,
        [Description("The context content in markdown")] string content,
        [Description("Comma-separated tags for categorization")] string? tags)
    {
        var existing = _store.Get(topic);
        if (existing is not null)
            return JsonSerializer.Serialize(new { error = $"Context '{topic}' already exists. Use update_context to modify it." });

        _store.Save(topic, tags, content);
        _logger.LogInformation("Saved context {Topic}", topic);

        return JsonSerializer.Serialize(new { status = "saved", topic });
    }

    [Description("Update an existing persistent context entry")]
    private string UpdateContext(
        [Description("The topic name of the context to update")] string topic,
        [Description("New content (replaces existing content)")] string content,
        [Description("New comma-separated tags (leave empty to keep current)")] string? tags)
    {
        var existing = _store.Get(topic);
        if (existing is null)
            return JsonSerializer.Serialize(new { error = $"Context '{topic}' not found" });

        var newTags = string.IsNullOrWhiteSpace(tags) ? existing.Tags : tags;

        _store.Save(topic, newTags, content);
        _logger.LogInformation("Updated context {Topic}", topic);

        return JsonSerializer.Serialize(new { status = "updated", topic });
    }

    [Description("List all persistent context entries")]
    private string ListContext()
    {
        var entries = _store.ListAll();
        return JsonSerializer.Serialize(new
        {
            count = entries.Count,
            entries = entries.Select(e => new
            {
                e.Topic,
                e.Tags,
                contentPreview = e.Content.Length > 150 ? e.Content[..150] + "..." : e.Content,
            }),
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Read the full content of a persistent context entry")]
    private string ReadContext(
        [Description("The topic name of the context to read")] string topic)
    {
        var entry = _store.Get(topic);
        if (entry is null)
            return JsonSerializer.Serialize(new { error = $"Context '{topic}' not found" });

        return JsonSerializer.Serialize(new
        {
            entry.Topic,
            entry.Tags,
            entry.Content,
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Delete a persistent context entry")]
    private string DeleteContext(
        [Description("The topic name of the context to delete")] string topic)
    {
        var deleted = _store.Delete(topic);
        return deleted
            ? JsonSerializer.Serialize(new { status = "deleted", topic })
            : JsonSerializer.Serialize(new { error = $"Context '{topic}' not found" });
    }
}
