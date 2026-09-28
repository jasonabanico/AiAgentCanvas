using AiAgentCanvas.Abstractions;
using Microsoft.Agents.AI;
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
        {
            // Prefer a judge model distinct from the one under test. Falling back to
            // the primary client keeps the capability usable, and the runner says so
            // in its output and its logs rather than presenting inflated scores as clean.
            var judge = sp.GetKeyedService<IChatClient>(AgentClientKeys.Judge);
            var independent = judge is not null;

            return new EvaluationRunner(
                () => sp.GetRequiredService<AIAgent>(),
                judge ?? sp.GetRequiredService<IChatClient>(),
                independent,
                sp.GetRequiredService<EvaluationStore>(),
                sp.GetRequiredService<ILogger<EvaluationRunner>>());
        });

        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            EvaluationToolProvider.CreateTools(
                sp.GetRequiredService<EvaluationStore>(),
                sp.GetRequiredService<EvaluationRunner>()));

        return services;
    }
}
