using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.Skills;

public sealed class SkillAuthoringToolProvider
{
    private readonly string _skillsDirectory;
    private readonly ILogger _logger;

    public SkillAuthoringToolProvider(string skillsDirectory, ILogger<SkillAuthoringToolProvider> logger)
    {
        _skillsDirectory = skillsDirectory;
        _logger = logger;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(AuthorSkill, "author_skill",
                "Create a new skill markdown file in the skills directory"),
            AIFunctionFactory.Create(EditSkill, "edit_skill",
                "Edit an existing skill markdown file"),
            AIFunctionFactory.Create(ReadSkill, "read_skill",
                "Read the contents of a skill markdown file"),
            AIFunctionFactory.Create(DeleteAuthoredSkill, "delete_authored_skill",
                "Delete a skill markdown file from the skills directory"),
        ];
    }

    [Description("Create a new skill markdown file in the skills directory")]
    private string AuthorSkill(
        [Description("Name of the skill")] string name,
        [Description("Description of what the skill does")] string description,
        [Description("Comma-separated tags for categorization")] string tags,
        [Description("The prompt template body with {input} placeholder")] string promptTemplate)
    {
        EnsureDirectory();

        var fileName = name.ToLowerInvariant().Replace(' ', '-') + ".md";
        var filePath = Path.Combine(_skillsDirectory, fileName);

        if (File.Exists(filePath))
            return JsonSerializer.Serialize(new { error = $"Skill file '{fileName}' already exists. Use edit_skill to modify it." });

        var content = BuildMarkdown(name, description, tags, promptTemplate);
        File.WriteAllText(filePath, content, Encoding.UTF8);

        _logger.LogInformation("Authored skill {Name} at {Path}", name, filePath);
        return JsonSerializer.Serialize(new { status = "created", name, file = fileName });
    }

    [Description("Edit an existing skill markdown file")]
    private string EditSkill(
        [Description("Name of the skill to edit")] string name,
        [Description("Updated description (leave empty to keep current)")] string? description,
        [Description("Updated comma-separated tags (leave empty to keep current)")] string? tags,
        [Description("Updated prompt template body (leave empty to keep current)")] string? promptTemplate)
    {
        var filePath = FindSkillFile(name);
        if (filePath is null)
            return JsonSerializer.Serialize(new { error = $"Skill '{name}' not found" });

        var content = File.ReadAllText(filePath);
        var parsed = ParseMarkdownSkill(content);
        if (parsed is null)
            return JsonSerializer.Serialize(new { error = $"Failed to parse skill file for '{name}'" });

        var (currentName, currentDescription, currentTags, currentBody) = parsed.Value;

        var updatedContent = BuildMarkdown(
            currentName,
            string.IsNullOrEmpty(description) ? currentDescription : description,
            string.IsNullOrEmpty(tags) ? currentTags : tags,
            string.IsNullOrEmpty(promptTemplate) ? currentBody : promptTemplate);

        File.WriteAllText(filePath, updatedContent, Encoding.UTF8);

        _logger.LogInformation("Edited skill {Name} at {Path}", name, filePath);
        return JsonSerializer.Serialize(new { status = "updated", name, file = Path.GetFileName(filePath) });
    }

    [Description("Read the contents of a skill markdown file")]
    private string ReadSkill(
        [Description("Name of the skill to read")] string name)
    {
        var filePath = FindSkillFile(name);
        if (filePath is null)
            return JsonSerializer.Serialize(new { error = $"Skill '{name}' not found" });

        var content = File.ReadAllText(filePath);
        var parsed = ParseMarkdownSkill(content);
        if (parsed is null)
            return JsonSerializer.Serialize(new { error = $"Failed to parse skill file for '{name}'" });

        var (skillName, description, skillTags, body) = parsed.Value;
        return JsonSerializer.Serialize(new
        {
            name = skillName,
            description,
            tags = skillTags,
            promptTemplate = body,
            file = Path.GetFileName(filePath),
        });
    }

    [Description("Delete a skill markdown file from the skills directory")]
    private string DeleteAuthoredSkill(
        [Description("Name of the skill to delete")] string name)
    {
        var filePath = FindSkillFile(name);
        if (filePath is null)
            return JsonSerializer.Serialize(new { error = $"Skill '{name}' not found" });

        File.Delete(filePath);
        _logger.LogInformation("Deleted skill {Name} at {Path}", name, filePath);
        return JsonSerializer.Serialize(new { status = "deleted", name, file = Path.GetFileName(filePath) });
    }

    private static string BuildMarkdown(string name, string description, string tags, string body)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"name: {name}");
        sb.AppendLine($"description: {description}");
        sb.AppendLine($"tags: {tags}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(body);
        return sb.ToString();
    }

    internal static (string name, string description, string tags, string body)? ParseMarkdownSkill(string content)
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

        var name = frontmatter.GetValueOrDefault("name", string.Empty);
        var description = frontmatter.GetValueOrDefault("description", string.Empty);
        var tags = frontmatter.GetValueOrDefault("tags", string.Empty);

        if (string.IsNullOrEmpty(name)) return null;

        return (name, description, tags, body);
    }

    private string? FindSkillFile(string name)
    {
        if (!Directory.Exists(_skillsDirectory)) return null;

        foreach (var file in Directory.GetFiles(_skillsDirectory, "*.md"))
        {
            try
            {
                var content = File.ReadAllText(file);
                var parsed = ParseMarkdownSkill(content);
                if (parsed is not null &&
                    parsed.Value.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }
            catch
            {
                // Skip files that can't be read or parsed
            }
        }

        return null;
    }

    private void EnsureDirectory()
    {
        if (!Directory.Exists(_skillsDirectory))
            Directory.CreateDirectory(_skillsDirectory);
    }
}
