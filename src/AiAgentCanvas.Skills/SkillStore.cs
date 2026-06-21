using AiAgentCanvas.Abstractions;

namespace AiAgentCanvas.Skills;

public class SkillRecord
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PromptTemplate { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

public sealed class SkillStore
{
    private readonly string _directory;

    public SkillStore(string directory)
    {
        _directory = directory;
        if (!Directory.Exists(_directory))
            Directory.CreateDirectory(_directory);
    }

    public void SaveSkill(SkillRecord skill)
    {
        MarkdownFile.Write(
            Path.Combine(_directory, MarkdownFile.SanitizeFileName(skill.Name) + ".md"),
            new Dictionary<string, string>
            {
                ["name"] = skill.Name,
                ["description"] = skill.Description,
            },
            skill.PromptTemplate);
    }

    public List<SkillRecord> ListSkills()
    {
        return MarkdownFile.LoadAll(_directory)
            .Select(ToRecord)
            .OrderBy(s => s.Name)
            .ToList();
    }

    public SkillRecord? GetSkill(string name)
    {
        var file = MarkdownFile.LoadAll(_directory)
            .FirstOrDefault(f => f.Get("name").Equals(name, StringComparison.OrdinalIgnoreCase));
        return file is null ? null : ToRecord(file);
    }

    public bool RemoveSkill(string name)
    {
        var file = MarkdownFile.LoadAll(_directory)
            .FirstOrDefault(f => f.Get("name").Equals(name, StringComparison.OrdinalIgnoreCase));
        if (file is null) return false;
        File.Delete(file.FilePath);
        return true;
    }

    private static SkillRecord ToRecord(MarkdownFile file) => new()
    {
        Name = file.Get("name"),
        Description = file.Get("description"),
        PromptTemplate = file.Body,
        FilePath = file.FilePath,
    };
}
