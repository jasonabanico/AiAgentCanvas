using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.AgentData.Entities;

public sealed class EntityToolProvider
{
    private readonly EntityStore _store;
    private readonly ILogger<EntityToolProvider> _logger;

    public EntityToolProvider(EntityStore store, ILogger<EntityToolProvider> logger)
    {
        _store = store;
        _logger = logger;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(SaveEntity, "save_entity",
                "Save a structured entity (person, company, project, etc.) to persistent memory"),
            AIFunctionFactory.Create(UpdateEntity, "update_entity",
                "Update an existing entity's attributes or relationships"),
            AIFunctionFactory.Create(ReadEntity, "read_entity",
                "Read the full details of a known entity"),
            AIFunctionFactory.Create(SearchEntities, "search_entities",
                "Search entities by name, type, tag, or content"),
            AIFunctionFactory.Create(ListEntities, "list_entities",
                "List all known entities grouped by type"),
            AIFunctionFactory.Create(DeleteEntity, "delete_entity",
                "Delete an entity from memory"),
        ];
    }

    [Description("Save a structured entity to persistent memory. Use markdown sections (## Attributes, ## Relationships, ## Notes) to organize information.")]
    private string SaveEntity(
        [Description("Entity name (e.g. 'Acme Corp', 'Jane Smith', 'Project Atlas')")] string name,
        [Description("Entity type (e.g. 'person', 'company', 'project', 'product', 'team')")] string type,
        [Description("Structured content using markdown. Use ## sections for Attributes, Relationships, and Notes")] string content,
        [Description("Comma-separated tags for categorization (e.g. 'client,enterprise' or 'engineering,backend')")] string? tags = null)
    {
        var existing = _store.Get(name);
        if (existing is not null)
            return JsonSerializer.Serialize(new { error = $"Entity '{name}' already exists. Use update_entity to modify it." });

        _store.Save(name, type, tags, content);
        _logger.LogInformation("Saved entity {Name} [{Type}]", name, type);

        return JsonSerializer.Serialize(new { status = "saved", name, type });
    }

    [Description("Update an existing entity's attributes, relationships, or other content")]
    private string UpdateEntity(
        [Description("The name of the entity to update")] string name,
        [Description("New content (replaces existing content). Use markdown sections.")] string content,
        [Description("New type (leave empty to keep current)")] string? type = null,
        [Description("New comma-separated tags (leave empty to keep current)")] string? tags = null)
    {
        var existing = _store.Get(name);
        if (existing is null)
            return JsonSerializer.Serialize(new { error = $"Entity '{name}' not found" });

        var newType = string.IsNullOrWhiteSpace(type) ? existing.Type : type;
        var newTags = string.IsNullOrWhiteSpace(tags) ? existing.Tags : tags;

        _store.Save(name, newType, newTags, content);
        _logger.LogInformation("Updated entity {Name}", name);

        return JsonSerializer.Serialize(new { status = "updated", name });
    }

    [Description("Read the full details of a known entity")]
    private string ReadEntity(
        [Description("The name of the entity to read")] string name)
    {
        var entity = _store.Get(name);
        if (entity is null)
            return JsonSerializer.Serialize(new { error = $"Entity '{name}' not found" });

        return JsonSerializer.Serialize(new
        {
            entity.Name,
            entity.Type,
            entity.Tags,
            entity.Content,
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Search entities by name, type, tag, or content")]
    private string SearchEntities(
        [Description("Search query to match against entity names, types, tags, and content")] string query)
    {
        var results = _store.Search(query);
        return JsonSerializer.Serialize(new
        {
            count = results.Count,
            entities = results.Select(e => new
            {
                e.Name,
                e.Type,
                e.Tags,
                contentPreview = e.Content.Length > 150 ? e.Content[..150] + "..." : e.Content,
            }),
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("List all known entities grouped by type")]
    private string ListEntities()
    {
        var entities = _store.ListAll();
        var grouped = entities
            .GroupBy(e => e.Type, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(e => new { e.Name, e.Tags }).ToList());

        return JsonSerializer.Serialize(new
        {
            totalCount = entities.Count,
            byType = grouped,
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Delete an entity from memory")]
    private string DeleteEntity(
        [Description("The name of the entity to delete")] string name)
    {
        var deleted = _store.Delete(name);
        return deleted
            ? JsonSerializer.Serialize(new { status = "deleted", name })
            : JsonSerializer.Serialize(new { error = $"Entity '{name}' not found" });
    }
}
