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
                "List all persistent context entries, optionally filtered by type"),
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
        [Description("Context type for categorization (e.g. fact, reference, decision, feedback, or any custom type). Omit for general.")] string? type = null,
        [Description("Comma-separated tags for categorization")] string? tags = null)
    {
        var existing = _store.Get(topic);
        if (existing is not null)
            return JsonSerializer.Serialize(new { error = $"Context '{topic}' already exists. Use update_context to modify it." });

        _store.Save(topic, type?.ToLowerInvariant(), tags, content);
        _logger.LogInformation("Saved context {Topic} (type: {Type})", topic, type ?? "general");

        return JsonSerializer.Serialize(new { status = "saved", topic, type = type ?? "general" });
    }

    [Description("Update an existing persistent context entry")]
    private string UpdateContext(
        [Description("The topic name of the context to update")] string topic,
        [Description("New content (replaces existing content)")] string content,
        [Description("Context type for categorization (e.g. fact, reference, decision, feedback, or any custom type). Omit to keep current.")] string? type = null,
        [Description("New comma-separated tags (leave empty to keep current)")] string? tags = null)
    {
        var existing = _store.Get(topic);
        if (existing is null)
            return JsonSerializer.Serialize(new { error = $"Context '{topic}' not found" });

        var newType = string.IsNullOrWhiteSpace(type) ? existing.Type : type.ToLowerInvariant();
        var newTags = string.IsNullOrWhiteSpace(tags) ? existing.Tags : tags;

        _store.Save(topic, newType, newTags, content);
        _logger.LogInformation("Updated context {Topic} (type: {Type})", topic, newType ?? "general");

        return JsonSerializer.Serialize(new { status = "updated", topic, type = newType ?? "general" });
    }

    [Description("List all persistent context entries, optionally filtered by type")]
    private string ListContext(
        [Description("Filter by type (e.g. fact, reference, decision, feedback). Omit to list all.")] string? type = null)
    {
        var entries = string.IsNullOrWhiteSpace(type)
            ? _store.ListAll()
            : _store.ListByType(type);

        return JsonSerializer.Serialize(new
        {
            count = entries.Count,
            entries = entries.Select(e => new
            {
                e.Topic,
                type = e.Type ?? "general",
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
            type = entry.Type ?? "general",
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
