using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.AgentData.Guardrails;

public sealed class GuardrailToolProvider
{
    private readonly GuardrailStore _store;
    private readonly ILogger<GuardrailToolProvider> _logger;

    private static readonly HashSet<string> ValidSeverities = new(StringComparer.OrdinalIgnoreCase)
    {
        "critical", "warning", "info"
    };

    public GuardrailToolProvider(GuardrailStore store, ILogger<GuardrailToolProvider> logger)
    {
        _store = store;
        _logger = logger;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(CreateGuardrail, "create_guardrail",
                "Create a new guardrail or policy rule"),
            AIFunctionFactory.Create(UpdateGuardrail, "update_guardrail",
                "Update an existing guardrail's rule or severity"),
            AIFunctionFactory.Create(ListGuardrails, "list_guardrails",
                "List all guardrails and their status"),
            AIFunctionFactory.Create(ToggleGuardrail, "toggle_guardrail",
                "Enable or disable a guardrail"),
            AIFunctionFactory.Create(DeleteGuardrail, "delete_guardrail",
                "Delete a guardrail"),
        ];
    }

    [Description("Create a new guardrail or policy rule that constrains the agent's behavior")]
    private string CreateGuardrail(
        [Description("Name of the guardrail (e.g. 'no-pii-sharing', 'require-approval')")] string name,
        [Description("Severity level: 'critical', 'warning', or 'info'")] string severity,
        [Description("The rule or policy in natural language")] string rule)
    {
        if (!ValidSeverities.Contains(severity))
            return JsonSerializer.Serialize(new { error = $"Invalid severity '{severity}'. Must be 'critical', 'warning', or 'info'." });

        var existing = _store.Get(name);
        if (existing is not null)
            return JsonSerializer.Serialize(new { error = $"Guardrail '{name}' already exists. Use update_guardrail to modify it." });

        _store.Save(name, severity, true, rule);
        _logger.LogInformation("Created guardrail {Name} [{Severity}]", name, severity);

        return JsonSerializer.Serialize(new { status = "created", name, severity });
    }

    [Description("Update an existing guardrail's rule or severity")]
    private string UpdateGuardrail(
        [Description("The name of the guardrail to update")] string name,
        [Description("New rule content (replaces existing)")] string rule,
        [Description("New severity level (leave empty to keep current)")] string? severity)
    {
        var existing = _store.Get(name);
        if (existing is null)
            return JsonSerializer.Serialize(new { error = $"Guardrail '{name}' not found" });

        var newSeverity = string.IsNullOrWhiteSpace(severity) ? existing.Severity : severity;

        if (!ValidSeverities.Contains(newSeverity))
            return JsonSerializer.Serialize(new { error = $"Invalid severity '{newSeverity}'. Must be 'critical', 'warning', or 'info'." });

        _store.Save(name, newSeverity, existing.Enabled, rule);
        _logger.LogInformation("Updated guardrail {Name}", name);

        return JsonSerializer.Serialize(new { status = "updated", name });
    }

    [Description("List all guardrails and their status")]
    private string ListGuardrails()
    {
        var guardrails = _store.ListAll();
        return JsonSerializer.Serialize(new
        {
            count = guardrails.Count,
            activeCount = guardrails.Count(g => g.Enabled),
            guardrails = guardrails.Select(g => new
            {
                g.Name,
                g.Severity,
                g.Enabled,
                rulePreview = g.Rule.Length > 150 ? g.Rule[..150] + "..." : g.Rule,
            }),
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Enable or disable a guardrail")]
    private string ToggleGuardrail(
        [Description("The name of the guardrail to toggle")] string name,
        [Description("Set to true to enable, false to disable")] bool enabled)
    {
        var existing = _store.Get(name);
        if (existing is null)
            return JsonSerializer.Serialize(new { error = $"Guardrail '{name}' not found" });

        _store.Save(name, existing.Severity, enabled, existing.Rule);
        _logger.LogInformation("Toggled guardrail {Name} to {Enabled}", name, enabled);

        return JsonSerializer.Serialize(new { status = "toggled", name, enabled });
    }

    [Description("Delete a guardrail")]
    private string DeleteGuardrail(
        [Description("The name of the guardrail to delete")] string name)
    {
        var deleted = _store.Delete(name);
        return deleted
            ? JsonSerializer.Serialize(new { status = "deleted", name })
            : JsonSerializer.Serialize(new { error = $"Guardrail '{name}' not found" });
    }
}
