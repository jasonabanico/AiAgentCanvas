using System.ComponentModel;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Skills;

public sealed class SkillToolProvider
{
    private readonly SkillStore _store;
    private readonly IServiceProvider _sp;
    private readonly ILogger<SkillToolProvider> _logger;

    public SkillToolProvider(SkillStore store, IServiceProvider sp, ILogger<SkillToolProvider> logger)
    {
        _store = store;
        _sp = sp;
        _logger = logger;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(CreateSkill, "create_skill",
                "Create a reusable prompt skill that can be run later"),
            AIFunctionFactory.Create(ListSkills, "list_skills",
                "List all saved prompt skills"),
            AIFunctionFactory.Create(RunSkill, "run_skill",
                "Run a saved prompt skill with the given input"),
            AIFunctionFactory.Create(RemoveSkill, "remove_skill",
                "Remove a saved prompt skill by name"),
        ];
    }

    [Description("Create a reusable prompt skill that can be run later")]
    private string CreateSkill(
        [Description("Human-readable name for the skill")] string name,
        [Description("What the skill does")] string description,
        [Description("Prompt template with {input} placeholder")] string promptTemplate)
    {
        var normalizedName = name.ToLowerInvariant().Replace(' ', '_');

        var record = new SkillRecord
        {
            Name = normalizedName,
            Description = description,
            PromptTemplate = promptTemplate,
        };

        _store.SaveSkill(record);
        _logger.LogInformation("Created skill {Name}", normalizedName);

        return JsonSerializer.Serialize(new { status = "created", name = normalizedName, description });
    }

    [Description("List all saved prompt skills")]
    private string ListSkills()
    {
        var skills = _store.ListSkills();
        var result = skills.Select(s => new { s.Name, s.Description }).ToList();
        return JsonSerializer.Serialize(new { count = result.Count, skills = result });
    }

    [Description("Run a saved prompt skill with the given input")]
    private async Task<string> RunSkill(
        [Description("Name of the skill to run")] string name,
        [Description("Input text to pass to the skill")] string input,
        CancellationToken ct)
    {
        var skill = _store.GetSkill(name);
        if (skill is null)
            return JsonSerializer.Serialize(new { error = $"Skill '{name}' not found" });

        var prompt = skill.PromptTemplate.Replace("{input}", input);
        _logger.LogInformation("Running skill {Name} with input length {Length}", name, input.Length);

        var innerClient = _sp.GetRequiredService<IChatClient>();
        var tools = _sp.GetServices<IReadOnlyList<AITool>>().SelectMany(t => t).ToList();
        using var chatClient = new FunctionInvokingChatClient(innerClient);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, prompt),
        };

        var options = new ChatOptions { Tools = tools };
        var response = await chatClient.GetResponseAsync(messages, options, ct);
        var responseText = response.Text ?? string.Empty;

        _logger.LogInformation("Skill {Name} completed, response length {Length}", name, responseText.Length);

        return JsonSerializer.Serialize(new { skill = name, result = responseText });
    }

    [Description("Remove a saved prompt skill by name")]
    private string RemoveSkill(
        [Description("Name of the skill to remove")] string name)
    {
        var removed = _store.RemoveSkill(name);
        _logger.LogInformation("Remove skill {Name}: {Removed}", name, removed);

        return JsonSerializer.Serialize(new { name, removed });
    }
}
