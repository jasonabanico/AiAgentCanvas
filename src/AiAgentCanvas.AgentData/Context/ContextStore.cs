using System.Text;
using AiAgentCanvas.Abstractions;

namespace AiAgentCanvas.AgentData.Context;

public sealed class ContextEntry
{
    public string Topic { get; set; } = string.Empty;
    public string? Tags { get; set; }
    public string Content { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

public sealed class ContextStore
{
    private readonly string _directory;
    private readonly string _userDirectory;
    private readonly string[] _readDirectories;

    public ContextStore(string directory, string userDirectory, params string[] additionalDirectories)
    {
        _directory = directory;
        _userDirectory = userDirectory;
        if (!Directory.Exists(_directory))
            Directory.CreateDirectory(_directory);
        if (!Directory.Exists(_userDirectory))
            Directory.CreateDirectory(_userDirectory);

        var dirs = new List<string> { directory, userDirectory };
        foreach (var dir in additionalDirectories)
        {
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            dirs.Add(dir);
        }
        _readDirectories = dirs.ToArray();
    }

    public void Save(string topic, string? tags, string content)
    {
        MarkdownFile.Write(
            Path.Combine(_directory, MarkdownFile.SanitizeFileName(topic) + ".md"),
            new Dictionary<string, string>
            {
                ["topic"] = topic,
                ["tags"] = tags ?? "",
            },
            content);
    }

    public ContextEntry? Get(string topic)
    {
        return ListAll().FirstOrDefault(e =>
            e.Topic.Equals(topic, StringComparison.OrdinalIgnoreCase));
    }

    public List<ContextEntry> ListAll()
    {
        return _readDirectories
            .SelectMany(dir => MarkdownFile.LoadAll(dir))
            .Select(ToEntry)
            .Where(e => e is not null)
            .Cast<ContextEntry>()
            .ToList();
    }

    public string LoadAllContent()
    {
        var entries = ListAll();
        if (entries.Count == 0) return "";

        var sb = new StringBuilder();
        sb.AppendLine("\n## Persistent Context");
        foreach (var entry in entries)
        {
            sb.AppendLine($"\n### {entry.Topic}");
            sb.AppendLine(entry.Content);
        }
        return sb.ToString();
    }

    public bool Delete(string topic)
    {
        var entry = Get(topic);
        if (entry is null) return false;
        File.Delete(entry.FilePath);
        return true;
    }

    private static ContextEntry? ToEntry(MarkdownFile file)
    {
        var topic = file.Get("topic");
        if (string.IsNullOrEmpty(topic)) return null;

        return new ContextEntry
        {
            Topic = topic,
            Tags = file.Get("tags"),
            Content = file.Body,
            FilePath = file.FilePath,
        };
    }
}
