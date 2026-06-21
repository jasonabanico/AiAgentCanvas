using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.AgentData.Profiles;

public sealed class UserProfileToolProvider
{
    private readonly UserProfileStore _store;
    private readonly ILogger<UserProfileToolProvider> _logger;

    public UserProfileToolProvider(UserProfileStore store, ILogger<UserProfileToolProvider> logger)
    {
        _store = store;
        _logger = logger;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(CreateUserProfile, "create_user_profile",
                "Create a new user profile with role and preferences"),
            AIFunctionFactory.Create(UpdateUserProfile, "update_user_profile",
                "Update an existing user profile"),
            AIFunctionFactory.Create(SwitchUserProfile, "switch_user_profile",
                "Switch to a different user profile"),
            AIFunctionFactory.Create(ReadUserProfile, "read_user_profile",
                "Read the full details of a user profile"),
            AIFunctionFactory.Create(ListUserProfiles, "list_user_profiles",
                "List all user profiles"),
            AIFunctionFactory.Create(DeleteUserProfile, "delete_user_profile",
                "Delete a user profile"),
        ];
    }

    [Description("Create a new user profile with role and preferences")]
    private string CreateUserProfile(
        [Description("Profile name (e.g. 'john-doe', 'admin-user')")] string name,
        [Description("User's role (e.g. 'developer', 'manager', 'analyst')")] string role,
        [Description("User preferences, working style, and other details in markdown")] string content,
        [Description("User's timezone (e.g. 'America/New_York', 'UTC+10')")] string? timezone)
    {
        var existing = _store.Get(name);
        if (existing is not null)
            return JsonSerializer.Serialize(new { error = $"Profile '{name}' already exists. Use update_user_profile to modify it." });

        _store.Save(name, role, timezone, content);
        _logger.LogInformation("Created user profile {Name}", name);

        return JsonSerializer.Serialize(new { status = "created", name, role });
    }

    [Description("Update an existing user profile")]
    private string UpdateUserProfile(
        [Description("The name of the profile to update")] string name,
        [Description("New content (replaces existing)")] string content,
        [Description("New role (leave empty to keep current)")] string? role,
        [Description("New timezone (leave empty to keep current)")] string? timezone)
    {
        var existing = _store.Get(name);
        if (existing is null)
            return JsonSerializer.Serialize(new { error = $"Profile '{name}' not found" });

        var newRole = string.IsNullOrWhiteSpace(role) ? existing.Role : role;
        var newTimezone = string.IsNullOrWhiteSpace(timezone) ? existing.Timezone : timezone;

        _store.Save(name, newRole, newTimezone, content);
        _logger.LogInformation("Updated user profile {Name}", name);

        return JsonSerializer.Serialize(new { status = "updated", name });
    }

    [Description("Switch to a different user profile")]
    private string SwitchUserProfile(
        [Description("The name of the profile to switch to")] string name)
    {
        var profile = _store.Get(name);
        if (profile is null)
            return JsonSerializer.Serialize(new { error = $"Profile '{name}' not found" });

        _store.SetActiveProfile(name);
        _logger.LogInformation("Switched to user profile {Name}", name);

        return JsonSerializer.Serialize(new { status = "switched", profile = name });
    }

    [Description("Read the full details of a user profile")]
    private string ReadUserProfile(
        [Description("The name of the profile to read")] string name)
    {
        var profile = _store.Get(name);
        if (profile is null)
            return JsonSerializer.Serialize(new { error = $"Profile '{name}' not found" });

        return JsonSerializer.Serialize(new
        {
            profile.Name,
            profile.Role,
            profile.Timezone,
            profile.Content,
            isActive = profile.Name.Equals(_store.GetActiveProfileName(), StringComparison.OrdinalIgnoreCase),
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("List all user profiles")]
    private string ListUserProfiles()
    {
        var profiles = _store.ListAll();
        var activeName = _store.GetActiveProfileName();

        return JsonSerializer.Serialize(new
        {
            count = profiles.Count,
            activeProfile = activeName ?? "none",
            profiles = profiles.Select(p => new
            {
                p.Name,
                p.Role,
                p.Timezone,
                isActive = p.Name.Equals(activeName, StringComparison.OrdinalIgnoreCase),
            }),
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Delete a user profile")]
    private string DeleteUserProfile(
        [Description("The name of the profile to delete")] string name)
    {
        var deleted = _store.Delete(name);
        return deleted
            ? JsonSerializer.Serialize(new { status = "deleted", name })
            : JsonSerializer.Serialize(new { error = $"Profile '{name}' not found" });
    }
}
