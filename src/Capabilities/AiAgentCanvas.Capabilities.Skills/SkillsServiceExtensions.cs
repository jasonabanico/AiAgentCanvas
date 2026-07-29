using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Orchestration.Skills;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.Skills;

public static class SkillsServiceExtensions
{
    public static IServiceCollection AddAiAgentCanvasSkills(
        this IServiceCollection services,
        string directory = "./agent-data/skills")
    {
        var store = new SkillStore(directory);
        services.AddSingleton(store);
        services.AddSingleton<SkillToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
        {
            foreach (var seed in sp.GetServices<ISkillSeed>())
            {
                if (store.GetSkill(seed.Name) is null)
                    store.SaveSkill(new SkillRecord
                    {
                        Name = seed.Name,
                        Description = seed.Description,
                        PromptTemplate = seed.PromptTemplate,
                    });
            }
            return sp.GetRequiredService<SkillToolProvider>().GetTools();
        });
        return services;
    }

    public static IServiceCollection AddAiAgentCanvasMcp(this IServiceCollection services)
    {
        services.AddSingleton<McpConnectionManager>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
        {
            var manager = sp.GetRequiredService<McpConnectionManager>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
                .CreateLogger("McpStartup");
            foreach (var seed in sp.GetServices<IMcpConnectionSeed>())
            {
                try
                {
                    manager.ConnectAsync(seed, CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to connect to MCP server {Name} at {Endpoint} during startup",
                        seed.Name, seed.Endpoint);
                }
            }
            return manager.GetTools();
        });
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
