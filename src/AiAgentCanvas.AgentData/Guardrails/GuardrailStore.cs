using System.Text;
using AiAgentCanvas.Abstractions;

namespace AiAgentCanvas.AgentData.Guardrails;

public sealed class GuardrailEntry
{
    public string Name { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Rule { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

public sealed class GuardrailStore
{
    private readonly string _directory;
    private readonly string _userDirectory;
    private readonly string[] _readDirectories;

    public GuardrailStore(string directory, string userDirectory, params string[] additionalDirectories)
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

    public void Save(string name, string severity, bool enabled, string rule)
    {
        MarkdownFile.Write(
            Path.Combine(_directory, MarkdownFile.SanitizeFileName(name) + ".md"),
            new Dictionary<string, string>
            {
                ["name"] = name,
                ["severity"] = severity,
                ["enabled"] = enabled.ToString().ToLowerInvariant(),
            },
            rule);
    }

    public GuardrailEntry? Get(string name)
    {
        return ListAll().FirstOrDefault(e =>
            e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public List<GuardrailEntry> ListAll()
    {
        return _readDirectories
            .SelectMany(dir => MarkdownFile.LoadAll(dir))
            .Select(ToEntry)
            .Where(e => e is not null)
            .Cast<GuardrailEntry>()
            .ToList();
    }

    public List<GuardrailEntry> GetActive()
    {
        return ListAll().Where(e => e.Enabled).ToList();
    }

    public string LoadActiveRules()
    {
        var active = GetActive();
        if (active.Count == 0) return "";

        var sb = new StringBuilder();
        sb.AppendLine("\n## Guardrails & Policies");
        foreach (var rule in active)
        {
            sb.AppendLine($"\n[{rule.Severity.ToUpperInvariant()}] {rule.Name}");
            sb.AppendLine(rule.Rule);
        }
        return sb.ToString();
    }

    public bool Delete(string name)
    {
        var entry = Get(name);
        if (entry is null) return false;
        File.Delete(entry.FilePath);
        return true;
    }

    private static GuardrailEntry? ToEntry(MarkdownFile file)
    {
        var name = file.Get("name");
        if (string.IsNullOrEmpty(name)) return null;

        return new GuardrailEntry
        {
            Name = name,
            Severity = file.Get("severity", "info"),
            Enabled = file.GetBool("enabled", true),
            Rule = file.Body,
            FilePath = file.FilePath,
        };
    }
}
