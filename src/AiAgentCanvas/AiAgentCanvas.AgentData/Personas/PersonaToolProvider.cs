using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.AgentData.Personas;

public sealed class PersonaToolProvider
{
    private readonly PersonaStore _store;
    private readonly ILogger<PersonaToolProvider> _logger;

    public PersonaToolProvider(PersonaStore store, ILogger<PersonaToolProvider> logger)
    {
        _store = store;
        _logger = logger;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(CreatePersona, "create_persona",
                "Create a new persona with custom instructions"),
            AIFunctionFactory.Create(UpdatePersona, "update_persona",
                "Update an existing persona's description or instructions"),
            AIFunctionFactory.Create(ListPersonas, "list_personas",
                "List all available personas"),
            AIFunctionFactory.Create(SwitchPersona, "switch_persona",
                "Switch to a different persona"),
            AIFunctionFactory.Create(ReadPersona, "read_persona",
                "Read the full details of a persona"),
            AIFunctionFactory.Create(DeletePersona, "delete_persona",
                "Delete a persona"),
        ];
    }

    [Description("Create a new persona with custom instructions that change the agent's behavior")]
    private string CreatePersona(
        [Description("Name of the persona (e.g. 'code-reviewer', 'technical-writer')")] string name,
        [Description("Short description of what this persona does")] string description,
        [Description("Full instructions for the persona in markdown")] string instructions)
    {
        var existing = _store.GetPersona(name);
        if (existing is not null)
            return JsonSerializer.Serialize(new { error = $"Persona '{name}' already exists. Use update_persona to modify it." });

        _store.SavePersona(name, description, instructions);
        _logger.LogInformation("Created persona {Name}", name);

        return JsonSerializer.Serialize(new { status = "created", name });
    }

    [Description("Update an existing persona's description or instructions")]
    private string UpdatePersona(
        [Description("The name of the persona to update")] string name,
        [Description("New instructions (replaces existing)")] string instructions,
        [Description("New description (leave empty to keep current)")] string? description)
    {
        var existing = _store.GetPersona(name);
        if (existing is null)
            return JsonSerializer.Serialize(new { error = $"Persona '{name}' not found" });

        var newDescription = string.IsNullOrWhiteSpace(description) ? existing.Description : description;

        _store.SavePersona(name, newDescription, instructions);
        _logger.LogInformation("Updated persona {Name}", name);

        return JsonSerializer.Serialize(new { status = "updated", name });
    }

    [Description("List all available personas and which one is active")]
    private string ListPersonas()
    {
        var personas = _store.ListPersonas();
        var activeName = _store.GetActivePersonaName();

        return JsonSerializer.Serialize(new
        {
            count = personas.Count,
            activePersona = activeName ?? "default",
            personas = personas.Select(p => new
            {
                p.Name,
                p.Description,
                isActive = p.Name.Equals(activeName, StringComparison.OrdinalIgnoreCase),
            }),
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Switch to a different persona. Use 'default' to clear the active persona.")]
    private string SwitchPersona(
        [Description("The name of the persona to switch to, or 'default' to clear")] string name)
    {
        if (name.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            _store.SetActivePersona(null);
            _logger.LogInformation("Switched to default persona");
            return JsonSerializer.Serialize(new { status = "switched", persona = "default" });
        }

        var persona = _store.GetPersona(name);
        if (persona is null)
            return JsonSerializer.Serialize(new { error = $"Persona '{name}' not found" });

        _store.SetActivePersona(name);
        _logger.LogInformation("Switched to persona {Name}", name);

        return JsonSerializer.Serialize(new { status = "switched", persona = name });
    }

    [Description("Read the full details of a persona")]
    private string ReadPersona(
        [Description("The name of the persona to read")] string name)
    {
        var persona = _store.GetPersona(name);
        if (persona is null)
            return JsonSerializer.Serialize(new { error = $"Persona '{name}' not found" });

        return JsonSerializer.Serialize(new
        {
            persona.Name,
            persona.Description,
            persona.Instructions,
            isActive = persona.Name.Equals(_store.GetActivePersonaName(), StringComparison.OrdinalIgnoreCase),
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Delete a persona")]
    private string DeletePersona(
        [Description("The name of the persona to delete")] string name)
    {
        var deleted = _store.DeletePersona(name);
        return deleted
            ? JsonSerializer.Serialize(new { status = "deleted", name })
            : JsonSerializer.Serialize(new { error = $"Persona '{name}' not found" });
    }
}
