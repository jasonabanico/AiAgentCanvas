using System.Text;
using AiAgentCanvas.Abstractions;

namespace AiAgentCanvas.AgentData.Entities;

public sealed class EntityEntry
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Tags { get; set; }
    public string Content { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

public sealed class EntityStore
{
    private readonly string _directory;

    public EntityStore(string directory)
    {
        _directory = directory;
        if (!Directory.Exists(_directory))
            Directory.CreateDirectory(_directory);
    }

    public void Save(string name, string type, string? tags, string content)
    {
        MarkdownFile.Write(
            Path.Combine(_directory, MarkdownFile.SanitizeFileName(name) + ".md"),
            new Dictionary<string, string>
            {
                ["name"] = name,
                ["type"] = type,
                ["tags"] = tags ?? "",
            },
            content);
    }

    public EntityEntry? Get(string name)
    {
        return ListAll().FirstOrDefault(e =>
            e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public List<EntityEntry> ListAll()
    {
        return MarkdownFile.LoadAll(_directory)
            .Select(ToEntry)
            .Where(e => e is not null)
            .Cast<EntityEntry>()
            .ToList();
    }

    public List<EntityEntry> Search(string query)
    {
        var q = query.ToLowerInvariant();
        return ListAll().Where(e =>
            e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            e.Type.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            (e.Tags?.Contains(q, StringComparison.OrdinalIgnoreCase) == true) ||
            e.Content.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public string LoadEntityIndex()
    {
        var entities = ListAll();
        if (entities.Count == 0) return "";

        var sb = new StringBuilder();
        sb.AppendLine("\n## Known Entities");
        foreach (var group in entities.GroupBy(e => e.Type, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"\n### {group.Key}");
            foreach (var entity in group)
            {
                sb.AppendLine($"- {entity.Name}");
            }
        }
        sb.AppendLine("\nUse the read_entity tool to get full details on any entity.");
        return sb.ToString();
    }

    public bool Delete(string name)
    {
        var entry = Get(name);
        if (entry is null) return false;
        File.Delete(entry.FilePath);
        return true;
    }

    private static EntityEntry? ToEntry(MarkdownFile file)
    {
        var name = file.Get("name");
        if (string.IsNullOrEmpty(name)) return null;

        return new EntityEntry
        {
            Name = name,
            Type = file.Get("type", "unknown"),
            Tags = file.Get("tags"),
            Content = file.Body,
            FilePath = file.FilePath,
        };
    }
}
