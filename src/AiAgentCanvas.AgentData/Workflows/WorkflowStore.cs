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

    public WorkflowStore(string directory)
    {
        _directory = directory;
        if (!Directory.Exists(_directory))
            Directory.CreateDirectory(_directory);
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
        return MarkdownFile.LoadAll(_directory)
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
