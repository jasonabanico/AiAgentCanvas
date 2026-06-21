using System.Text;

namespace AiAgentCanvas.Abstractions;

public sealed class MarkdownFile
{
    public Dictionary<string, string> Frontmatter { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Body { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;

    public string Get(string key, string fallback = "") =>
        Frontmatter.TryGetValue(key, out var value) ? value : fallback;

    public bool GetBool(string key, bool fallback = false) =>
        Frontmatter.TryGetValue(key, out var value) && bool.TryParse(value, out var result) ? result : fallback;

    public static MarkdownFile? Parse(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        return ParseContent(File.ReadAllText(filePath), filePath);
    }

    public static MarkdownFile? ParseContent(string content, string filePath = "")
    {
        if (!content.StartsWith("---")) return null;

        var endIndex = content.IndexOf("---", 3, StringComparison.Ordinal);
        if (endIndex < 0) return null;

        var frontmatterBlock = content[3..endIndex].Trim();
        var body = content[(endIndex + 3)..].Trim();

        var frontmatter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in frontmatterBlock.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var colonIdx = line.IndexOf(':');
            if (colonIdx < 0) continue;
            var key = line[..colonIdx].Trim();
            var value = line[(colonIdx + 1)..].Trim();
            frontmatter[key] = value;
        }

        return new MarkdownFile
        {
            Frontmatter = frontmatter,
            Body = body,
            FilePath = filePath,
        };
    }

    public static void Write(string filePath, Dictionary<string, string> frontmatter, string body)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.AppendLine("---");
        foreach (var (key, value) in frontmatter)
        {
            if (!string.IsNullOrWhiteSpace(value))
                sb.AppendLine($"{key}: {value}");
        }
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(body);

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    public static List<MarkdownFile> LoadAll(string directory, string pattern = "*.md")
    {
        var files = new List<MarkdownFile>();
        if (!Directory.Exists(directory)) return files;

        foreach (var file in Directory.GetFiles(directory, pattern))
        {
            var parsed = Parse(file);
            if (parsed is not null)
                files.Add(parsed);
        }
        return files;
    }

    public static string SanitizeFileName(string name)
    {
        return name.ToLowerInvariant()
            .Replace(' ', '-')
            .Replace('_', '-');
    }
}
