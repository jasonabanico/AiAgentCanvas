using AiAgentCanvas.Abstractions;

namespace AiAgentCanvas.AgentData.Workflows;

public sealed class WorkflowStep
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Tool { get; set; } = string.Empty;
    public string? Input { get; set; }
}

public sealed class WorkflowDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Tags { get; set; }
    public string Content { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

public sealed class WorkflowStore
{
    private readonly string _directory;
    private readonly string _userDirectory;
    private readonly string[] _readDirectories;

    public WorkflowStore(string directory, string userDirectory, params string[] additionalDirectories)
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

    public void Save(string name, string description, string? tags, string content)
    {
        MarkdownFile.Write(
            Path.Combine(_directory, MarkdownFile.SanitizeFileName(name) + ".md"),
            new Dictionary<string, string>
            {
                ["name"] = name,
                ["description"] = description,
                ["tags"] = tags ?? "",
            },
            content);
    }

    public WorkflowDefinition? Get(string name)
    {
        return ListAll().FirstOrDefault(w =>
            w.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public List<WorkflowDefinition> ListAll()
    {
        return _readDirectories
            .SelectMany(dir => MarkdownFile.LoadAll(dir))
            .Select(ToWorkflow)
            .Where(w => w is not null)
            .Cast<WorkflowDefinition>()
            .ToList();
    }

    public bool Delete(string name)
    {
        var workflow = Get(name);
        if (workflow is null) return false;
        File.Delete(workflow.FilePath);
        return true;
    }

    private static WorkflowDefinition? ToWorkflow(MarkdownFile file)
    {
        var name = file.Get("name");
        if (string.IsNullOrEmpty(name)) return null;

        return new WorkflowDefinition
        {
            Name = name,
            Description = file.Get("description"),
            Tags = file.Get("tags"),
            Content = file.Body,
            FilePath = file.FilePath,
        };
    }
}
