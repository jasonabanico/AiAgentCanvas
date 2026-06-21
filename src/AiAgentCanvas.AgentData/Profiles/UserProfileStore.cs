using System.Text;
using AiAgentCanvas.Abstractions;

namespace AiAgentCanvas.AgentData.Profiles;

public sealed class UserProfile
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? Timezone { get; set; }
    public string Content { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

public sealed class UserProfileStore
{
    private readonly string _directory;
    private readonly string _activeFilePath;

    public UserProfileStore(string directory)
    {
        _directory = directory;
        _activeFilePath = Path.Combine(_directory, ".active");
        if (!Directory.Exists(_directory))
            Directory.CreateDirectory(_directory);
    }

    public void Save(string name, string role, string? timezone, string content)
    {
        MarkdownFile.Write(
            Path.Combine(_directory, MarkdownFile.SanitizeFileName(name) + ".md"),
            new Dictionary<string, string>
            {
                ["name"] = name,
                ["role"] = role,
                ["timezone"] = timezone ?? "",
            },
            content);
    }

    public UserProfile? Get(string name)
    {
        return ListAll().FirstOrDefault(p =>
            p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public UserProfile? GetActive()
    {
        var activeName = GetActiveProfileName();
        if (activeName is null) return null;
        return Get(activeName);
    }

    public string LoadActiveProfileContext()
    {
        var profile = GetActive();
        if (profile is null) return "";

        var sb = new StringBuilder();
        sb.AppendLine("\n## Active User Profile");
        sb.AppendLine($"- Name: {profile.Name}");
        sb.AppendLine($"- Role: {profile.Role}");
        if (!string.IsNullOrEmpty(profile.Timezone))
            sb.AppendLine($"- Timezone: {profile.Timezone}");
        if (!string.IsNullOrEmpty(profile.Content))
        {
            sb.AppendLine();
            sb.AppendLine(profile.Content);
        }
        return sb.ToString();
    }

    public List<UserProfile> ListAll()
    {
        return MarkdownFile.LoadAll(_directory)
            .Select(ToProfile)
            .Where(p => p is not null)
            .Cast<UserProfile>()
            .ToList();
    }

    public bool Delete(string name)
    {
        var profile = Get(name);
        if (profile is null) return false;

        var activeName = GetActiveProfileName();
        if (activeName is not null && activeName.Equals(name, StringComparison.OrdinalIgnoreCase))
            SetActiveProfile(null);

        File.Delete(profile.FilePath);
        return true;
    }

    public void SetActiveProfile(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            if (File.Exists(_activeFilePath))
                File.Delete(_activeFilePath);
        }
        else
        {
            File.WriteAllText(_activeFilePath, name);
        }
    }

    public string? GetActiveProfileName()
    {
        if (!File.Exists(_activeFilePath)) return null;
        var name = File.ReadAllText(_activeFilePath).Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    private static UserProfile? ToProfile(MarkdownFile file)
    {
        var name = file.Get("name");
        if (string.IsNullOrEmpty(name)) return null;

        return new UserProfile
        {
            Name = name,
            Role = file.Get("role"),
            Timezone = file.Get("timezone"),
            Content = file.Body,
            FilePath = file.FilePath,
        };
    }
}
