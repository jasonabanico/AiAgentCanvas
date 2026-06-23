using AiAgentCanvas.Abstractions;

namespace AiAgentCanvas.AgentData.Goals;

public sealed class GoalEntry
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Priority { get; set; } = "medium";
    public string Status { get; set; } = "active";
    public string AcceptanceCriteria { get; set; } = string.Empty;
    public string? AssignedAgent { get; set; }
    public string Content { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

public sealed class GoalStore
{
    private readonly string _directory;
    private readonly string _userDirectory;
    private readonly string[] _readDirectories;

    public GoalStore(string directory, string userDirectory, params string[] additionalDirectories)
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

    public void Save(string name, string description, string priority, string status, string acceptanceCriteria, string? assignedAgent, string content)
    {
        MarkdownFile.Write(
            Path.Combine(_directory, MarkdownFile.SanitizeFileName(name) + ".md"),
            new Dictionary<string, string>
            {
                ["name"] = name,
                ["description"] = description,
                ["priority"] = priority,
                ["status"] = status,
                ["acceptance_criteria"] = acceptanceCriteria,
                ["assigned_agent"] = assignedAgent ?? "",
            },
            content);
    }

    public GoalEntry? Get(string name)
    {
        return ListAll().FirstOrDefault(g =>
            g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public List<GoalEntry> ListAll()
    {
        return _readDirectories
            .SelectMany(dir => MarkdownFile.LoadAll(dir))
            .Select(ToEntry)
            .Where(e => e is not null)
            .Cast<GoalEntry>()
            .ToList();
    }

    public List<GoalEntry> ListActive()
    {
        return ListAll()
            .Where(g => g.Status.Equals("active", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(g => g.Priority switch
            {
                "critical" => 4,
                "high" => 3,
                "medium" => 2,
                "low" => 1,
                _ => 0,
            })
            .ToList();
    }

    public bool UpdateStatus(string name, string newStatus)
    {
        var goal = Get(name);
        if (goal is null) return false;
        Save(goal.Name, goal.Description, goal.Priority, newStatus, goal.AcceptanceCriteria, goal.AssignedAgent, goal.Content);
        return true;
    }

    public bool Delete(string name)
    {
        var goal = Get(name);
        if (goal is null) return false;
        File.Delete(goal.FilePath);
        return true;
    }

    private static GoalEntry? ToEntry(MarkdownFile file)
    {
        var name = file.Get("name");
        if (string.IsNullOrEmpty(name)) return null;

        return new GoalEntry
        {
            Name = name,
            Description = file.Get("description") ?? "",
            Priority = file.Get("priority") ?? "medium",
            Status = file.Get("status") ?? "active",
            AcceptanceCriteria = file.Get("acceptance_criteria") ?? "",
            AssignedAgent = file.Get("assigned_agent"),
            Content = file.Body,
            FilePath = file.FilePath,
        };
    }
}
