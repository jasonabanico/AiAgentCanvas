#pragma warning disable MEAI001

using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Orchestration.Services;

/// <summary>
/// The chat client pipeline and tool wrapping every agent runs on. The default agent
/// and the persona agents built by the registry share it, so a limit, a cost counter
/// or a governance rule applies to a delegated agent exactly as it does to the first
/// one. Persona agents used to be built on the raw provider client and the raw tool
/// list, which left them outside all of it.
/// </summary>
public static class AgentPipeline
{
    public static IChatClient BuildChatClient(IServiceProvider sp)
    {
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var rawChatClient = sp.GetRequiredService<IChatClient>();
        var tokenCounter = sp.GetRequiredService<ITokenCounter>();

        // Order matters. Budget enforcement runs closest to the provider so it sees
        // the final prompt, and the loop guard runs outside it so a terminated run
        // never pays for compaction it will not use.
        IChatClient pipeline = new ToolDeduplicatingChatClient(
            rawChatClient, loggerFactory.CreateLogger<ToolDeduplicatingChatClient>());

        pipeline = new CostTrackingChatClient(
            pipeline,
            sp.GetRequiredService<ModelPricingOptions>(),
            loggerFactory.CreateLogger<CostTrackingChatClient>());

        var budgetOptions = sp.GetRequiredService<ContextBudgetOptions>();
        if (budgetOptions.Enabled)
        {
            pipeline = new ContextBudgetChatClient(
                pipeline,
                budgetOptions,
                tokenCounter,
                sp.GetKeyedService<IChatClient>(AgentClientKeys.Economy),
                loggerFactory.CreateLogger<ContextBudgetChatClient>());
        }

        // Outside the budget, so the tool list is narrowed by relevance before the budget
        // falls back to dropping tools by position.
        var selectionOptions = sp.GetService<ToolSelectionOptions>();
        if (selectionOptions is { Enabled: true })
        {
            pipeline = new ToolSelectingChatClient(
                pipeline,
                selectionOptions,
                sp.GetService<IEmbeddingGenerator<string, Embedding<float>>>(),
                loggerFactory.CreateLogger<ToolSelectingChatClient>());
        }

        var routerOptions = sp.GetService<ModelRouterOptions>();
        if (routerOptions is not null)
        {
            routerOptions.EconomyClient ??= sp.GetKeyedService<IChatClient>(AgentClientKeys.Economy);
            if (routerOptions.EconomyClient is null)
            {
                loggerFactory.CreateLogger<CostAwareModelRouter>().LogWarning(
                    "Agent:ModelRouter is enabled but no economy model is configured, so every turn uses the primary model.");
            }
            pipeline = new CostAwareModelRouter(
                pipeline, routerOptions, loggerFactory.CreateLogger<CostAwareModelRouter>());
        }

        var auditClient = sp.GetService<IAuditingChatClientFactory>();
        if (auditClient is not null)
            pipeline = auditClient.Wrap(pipeline);

        var reflectiveOptions = sp.GetService<ReflectiveOptions>();
        if (reflectiveOptions is not null)
        {
            pipeline = new ReflectiveChatClient(
                pipeline, reflectiveOptions, loggerFactory.CreateLogger<ReflectiveChatClient>());
        }

        var loopGuardOptions = sp.GetRequiredService<LoopGuardOptions>();
        if (loopGuardOptions.Enabled)
        {
            pipeline = new LoopGuardChatClient(
                pipeline, loopGuardOptions, tokenCounter, loggerFactory.CreateLogger<LoopGuardChatClient>());
        }

        return pipeline;
    }

    /// <summary>
    /// Wraps each function in the governance policy (when registered), the output cap and the
    /// tracing wrapper. Tools that are not functions pass through unchanged.
    /// </summary>
    public static List<AITool> WrapTools(IServiceProvider sp, IEnumerable<AITool> rawTools)
    {
        var governanceWrapper = sp.GetService<IToolGovernanceWrapper>();
        var outputOptions = sp.GetService<ToolOutputOptions>();

        return rawTools.Select(t =>
        {
            if (t is not AIFunction fn) return t;
            if (governanceWrapper is not null) fn = governanceWrapper.Wrap(fn);
            if (outputOptions is { Enabled: true }) fn = new BoundedOutputAIFunction(fn, outputOptions);
            return (AITool)new TracedAIFunction(fn);
        }).ToList();
    }
}
