using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.Evaluation;

public static class EvaluationServiceExtensions
{
    public static IServiceCollection AddAiAgentCanvasEvaluation(
        this IServiceCollection services,
        string? dbPath = null)
    {
        var path = dbPath ?? Path.Combine(Directory.GetCurrentDirectory(), "evaluation.db");

        services.AddSingleton(sp =>
            new EvaluationStore(path, sp.GetRequiredService<ILogger<EvaluationStore>>()));

        services.AddSingleton(sp =>
            new EvaluationRunner(
                sp.GetRequiredService<IChatClient>(),
                sp.GetRequiredService<EvaluationStore>(),
                sp.GetRequiredService<ILogger<EvaluationRunner>>()));

        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            EvaluationToolProvider.CreateTools(
                sp.GetRequiredService<EvaluationStore>(),
                sp.GetRequiredService<EvaluationRunner>()));

        return services;
    }
}
