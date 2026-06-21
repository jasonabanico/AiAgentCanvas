using AiAgentCanvas.Core.Skills;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.Skills;

public static class SkillsServiceExtensions
{
    public static IServiceCollection AddAiAgentCanvasSkills(
        this IServiceCollection services,
        string connectionString = "Data Source=skills.db")
    {
        services.AddSingleton(new SkillStore(connectionString));
        services.AddSingleton<SkillToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<SkillToolProvider>().GetTools());
        return services;
    }

    public static IServiceCollection AddAiAgentCanvasMcp(this IServiceCollection services)
    {
        services.AddSingleton<McpConnectionManager>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<McpConnectionManager>().GetTools());
        return services;
    }

    public static IServiceCollection AddAiAgentCanvasSkillRegistry(
        this IServiceCollection services,
        string skillsDirectory = "./agent-data/skills")
    {
        services.AddSingleton(sp => new LocalSkillRegistry(
            skillsDirectory,
            sp.GetRequiredService<SkillStore>(),
            sp.GetRequiredService<McpConnectionManager>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LocalSkillRegistry>>()));
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<LocalSkillRegistry>().GetTools());
        return services;
    }

    public static IServiceCollection AddAiAgentCanvasSkillAuthoring(
        this IServiceCollection services,
        string skillsDirectory = "./agent-data/skills")
    {
        services.AddSingleton(sp => new SkillAuthoringToolProvider(
            skillsDirectory,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SkillAuthoringToolProvider>>()));
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<SkillAuthoringToolProvider>().GetTools());
        return services;
    }
}
