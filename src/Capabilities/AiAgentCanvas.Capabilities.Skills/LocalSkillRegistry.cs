using System.ComponentModel;
using System.Text.Json;
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.Skills;

public class SkillManifest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? PromptTemplate { get; set; }
    public string? McpEndpoint { get; set; }
    public string? McpTransport { get; set; }
    public List<string> Tags { get; set; } = [];
}

public sealed class LocalSkillRegistry
{
    private readonly string _skillsDirectory;
    private readonly SkillStore _store;
    private readonly McpConnectionManager _mcpManager;
    private readonly ILogger _logger;

    public LocalSkillRegistry(
        string skillsDirectory,
        SkillStore store,
        McpConnectionManager mcpManager,
        ILogger<LocalSkillRegistry> logger)
    {
        _skillsDirectory = skillsDirectory;
        _store = store;
        _mcpManager = mcpManager;
        _logger = logger;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(SearchAvailableSkills, "search_available_skills",
                "Search the local skill catalog by keyword"),
            AIFunctionFactory.Create(InstallSkill, "install_skill",
                "Install a skill from the local catalog"),
            AIFunctionFactory.Create(ListSkillCatalog, "list_skill_catalog",
                "List all skills in the local catalog with their installed status"),
        ];
    }

    [Description("Search the local skill catalog by keyword")]
    private string SearchAvailableSkills(
        [Description("Keyword to search for in skill names, descriptions, and tags")] string query)
    {
        var manifests = LoadAllManifests();
        var queryLower = query.ToLowerInvariant();

        var matches = manifests.Where(m =>
            m.Name.Contains(queryLower, StringComparison.OrdinalIgnoreCase) ||
            m.Description.Contains(queryLower, StringComparison.OrdinalIgnoreCase) ||
            m.Tags.Any(t => t.Contains(queryLower, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var results = matches.Select(m => new
        {
            m.Name,
            m.Description,
            m.Tags,
            isMcp = !string.IsNullOrEmpty(m.McpEndpoint),
        }).ToList();

        return JsonSerializer.Serialize(new { count = results.Count, skills = results });
    }

    [Description("Install a skill from the local catalog")]
    private string InstallSkill(
        [Description("Name of the skill to install")] string name)
    {
        var manifests = LoadAllManifests();
        var manifest = manifests.FirstOrDefault(m =>
            m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (manifest is null)
            return JsonSerializer.Serialize(new { error = $"Skill '{name}' not found in catalog" });

        if (!string.IsNullOrEmpty(manifest.McpEndpoint))
        {
            _logger.LogInformation("Skill {Name} is an MCP skill, returning connection instructions", name);
            return JsonSerializer.Serialize(new
            {
                status = "mcp_skill",
                instruction = $"Use connect_mcp_server with name='{manifest.Name}', endpoint='{manifest.McpEndpoint}', transport='{manifest.McpTransport ?? "http"}'",
                name = manifest.Name,
                endpoint = manifest.McpEndpoint,
                transport = manifest.McpTransport ?? "http",
            });
        }

        var record = new SkillRecord
        {
            Name = manifest.Name.ToLowerInvariant().Replace(' ', '_'),
            Description = manifest.Description,
            PromptTemplate = manifest.PromptTemplate ?? string.Empty,
        };

        _store.SaveSkill(record);
        _logger.LogInformation("Installed skill {Name} from catalog", name);

        return JsonSerializer.Serialize(new { status = "installed", name = record.Name, description = record.Description });
    }

    [Description("List all skills in the local catalog with their installed status")]
    private string ListSkillCatalog()
    {
        var manifests = LoadAllManifests();
        var installedSkills = _store.ListSkills();

        var catalog = manifests.Select(m =>
        {
            var installed = installedSkills.Any(s =>
                s.Name.Equals(m.Name.ToLowerInvariant().Replace(' ', '_'), StringComparison.OrdinalIgnoreCase));

            return new
            {
                m.Name,
                m.Description,
                m.Tags,
                isMcp = !string.IsNullOrEmpty(m.McpEndpoint),
                installed,
            };
        }).ToList();

        return JsonSerializer.Serialize(new { count = catalog.Count, skills = catalog });
    }

    private List<SkillManifest> LoadAllManifests()
    {
        var manifests = new List<SkillManifest>();
        if (!Directory.Exists(_skillsDirectory)) return manifests;

        foreach (var jsonFile in Directory.GetFiles(_skillsDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(jsonFile);
                var manifest = JsonSerializer.Deserialize<SkillManifest>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (manifest is not null)
                    manifests.Add(manifest);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse skill manifest {File}", jsonFile);
            }
        }

        foreach (var mdFile in Directory.GetFiles(_skillsDirectory, "*.md"))
        {
            try
            {
                var parsed = MarkdownFile.Parse(mdFile);
                if (parsed is null) continue;

                var manifest = new SkillManifest
                {
                    Name = parsed.Get("name"),
                    Description = parsed.Get("description"),
                    PromptTemplate = parsed.Body,
                    Tags = parsed.Get("tags")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList(),
                };

                if (!string.IsNullOrEmpty(manifest.Name))
                    manifests.Add(manifest);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse skill markdown {File}", mdFile);
            }
        }

        return manifests;
    }
}
