using System.Collections.Concurrent;
using System.Text.Json;
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.StructuredOutput;

/// <summary>
/// Schemas by name. Files in the schema directory are read once at start. Code can add more
/// with <see cref="Add"/>. A file that is not valid JSON is skipped with a warning, so one bad
/// file does not hide the others.
/// </summary>
public sealed class SchemaCatalog : ISchemaCatalog
{
    private readonly ConcurrentDictionary<string, (JsonElement Schema, string? Description)> _schemas =
        new(StringComparer.OrdinalIgnoreCase);

    public SchemaCatalog(StructuredOptions options, ILogger<SchemaCatalog> logger)
    {
        var directory = Path.IsPathRooted(options.SchemaDirectory)
            ? options.SchemaDirectory
            : Path.Combine(Directory.GetCurrentDirectory(), options.SchemaDirectory);

        if (!Directory.Exists(directory))
            return;

        foreach (var file in Directory.EnumerateFiles(directory, "*.schema.json"))
        {
            var name = Path.GetFileName(file)[..^".schema.json".Length];
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                var schema = document.RootElement.Clone();
                var description = schema.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
                    ? d.GetString()
                    : null;
                _schemas[name] = (schema, description);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Skipped schema file {File}: it is not valid JSON", file);
            }
        }
    }

    public IReadOnlyList<string> Names => _schemas.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

    public bool TryGet(string name, out JsonElement schema, out string? description)
    {
        if (_schemas.TryGetValue(name, out var entry))
        {
            schema = entry.Schema;
            description = entry.Description;
            return true;
        }

        schema = default;
        description = null;
        return false;
    }

    public void Add(string name, JsonElement schema, string? description = null) =>
        _schemas[name] = (schema.Clone(), description);
}
