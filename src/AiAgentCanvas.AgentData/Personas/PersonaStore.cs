using AiAgentCanvas.Abstractions;

namespace AiAgentCanvas.AgentData.Personas;

public sealed class PersonaInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

public sealed class PersonaStore
{
    private readonly string _directory;
    private readonly string _activeFilePath;

    public PersonaStore(string directory)
    {
        _directory = directory;
        _activeFilePath = Path.Combine(_directory, ".active");
        if (!Directory.Exists(_directory))
            Directory.CreateDirectory(_directory);
    }

    public string? GetActivePersonaName()
    {
        if (!File.Exists(_activeFilePath)) return null;
        var name = File.ReadAllText(_activeFilePath).Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    public void SetActivePersona(string? name)
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

    public string? GetActiveInstructions()
    {
        var activeName = GetActivePersonaName();
        if (activeName is null) return null;

        var persona = GetPersona(activeName);
        return persona?.Instructions;
    }

    public void SavePersona(string name, string description, string instructions)
    {
        MarkdownFile.Write(
            Path.Combine(_directory, MarkdownFile.SanitizeFileName(name) + ".md"),
            new Dictionary<string, string>
            {
                ["name"] = name,
                ["description"] = description,
            },
            instructions);
    }

    public PersonaInfo? GetPersona(string name)
    {
        return ListPersonas().FirstOrDefault(p =>
            p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public List<PersonaInfo> ListPersonas()
    {
        return MarkdownFile.LoadAll(_directory)
            .Select(ToPersona)
            .Where(p => p is not null)
            .Cast<PersonaInfo>()
            .ToList();
    }

    public bool DeletePersona(string name)
    {
        var persona = GetPersona(name);
        if (persona is null) return false;

        var activeName = GetActivePersonaName();
        if (activeName is not null && activeName.Equals(name, StringComparison.OrdinalIgnoreCase))
            SetActivePersona(null);

        File.Delete(persona.FilePath);
        return true;
    }

    private static PersonaInfo? ToPersona(MarkdownFile file)
    {
        var name = file.Get("name");
        if (string.IsNullOrEmpty(name)) return null;

        return new PersonaInfo
        {
            Name = name,
            Description = file.Get("description"),
            Instructions = file.Body,
            FilePath = file.FilePath,
        };
    }
}
