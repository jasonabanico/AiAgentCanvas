#pragma warning disable MAAI001

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.EpisodicMemory;

public static class EpisodicMemoryServiceExtensions
{
    public static IServiceCollection AddAiAgentCanvasEpisodicMemory(
        this IServiceCollection services,
        string? dbPath = null)
    {
        var path = dbPath ?? Path.Combine(Directory.GetCurrentDirectory(), "episodic-memory.db");

        services.AddSingleton(sp =>
            new EpisodicMemoryStore(path, sp.GetRequiredService<ILogger<EpisodicMemoryStore>>()));

        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            EpisodicMemoryToolProvider.CreateTools(sp.GetRequiredService<EpisodicMemoryStore>()));

        services.AddSingleton<AIContextProvider>(sp =>
            new EpisodicMemoryContextProvider(
                sp.GetRequiredService<EpisodicMemoryStore>(),
                sp.GetRequiredService<ILogger<EpisodicMemoryContextProvider>>()));

        services.AddHostedService<MemoryDecayService>();

        return services;
    }
}
