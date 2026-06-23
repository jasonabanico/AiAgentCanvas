using System.Diagnostics;
using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Core.Agents;
using AiAgentCanvas.Core.Configuration;
using AiAgentCanvas.Core.Endpoints;
using AiAgentCanvas.Core.Providers;
using AiAgentCanvas.Core.Services;
using AiAgentCanvas.Core.Skills;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace AiAgentCanvas.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAiAgentCanvas(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<AiAgentCanvasOptions>? configure = null)
    {
        services.Configure<AIFoundryOptions>(configuration.GetSection(AIFoundryOptions.SectionName));
        services.AddSingleton<AIFoundryClientFactory>();
        services.AddSingleton<IChatClient>(sp =>
            new ToolDeduplicatingChatClient(sp.GetRequiredService<AIFoundryClientFactory>().CreateChatClient()));
        services.AddHttpClient();

        var options = new AiAgentCanvasOptions();
        configure?.Invoke(options);

        services.AddSingleton<DynamicToolRegistry>();

        var defaultPrompt = options.SystemPrompt ?? "You are a helpful AI assistant. Use the available tools to help answer questions.";
        services.AddSingleton(new DefaultSystemPrompt(defaultPrompt));

        services.AddSingleton<AIContextProvider>(new SystemPromptProvider(defaultPrompt));

        services.AddSingleton<AIContextProvider>(sp =>
            new DynamicToolContextProvider(sp.GetRequiredService<DynamicToolRegistry>()));

        services.AddSingleton(sp =>
            new PlanningMiddleware(
                sp.GetRequiredService<IChatClient>(),
                sp.GetRequiredService<DynamicToolRegistry>(),
                sp.GetServices<IReadOnlyList<AITool>>(),
                sp.GetRequiredService<ILoggerFactory>()));

        services.AddSingleton(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var chatHistoryProvider = sp.GetService<ChatHistoryProvider>();
            var contextProviders = sp.GetServices<AIContextProvider>().ToList();

            var rawTools = sp.GetServices<IReadOnlyList<AITool>>().SelectMany(t => t).ToList();
            var governanceWrapper = sp.GetService<IToolGovernanceWrapper>();
            var tools = governanceWrapper is not null
                ? rawTools.Select(t => t is AIFunction fn ? (AITool)governanceWrapper.Wrap(fn) : t).ToList()
                : rawTools;
            var toolLogger = loggerFactory.CreateLogger("AiAgentCanvas.ToolRegistration");
            toolLogger.LogInformation("Registered {ToolCount} tools (governance={Governed}): {ToolNames}",
                tools.Count, governanceWrapper is not null, string.Join(", ", tools.Select(t => t.Name)));

            var toolNames = new HashSet<string>(tools.Select(t => t.Name));
            foreach (var dep in sp.GetServices<IToolDependencySeed>())
            {
                var missing = dep.RequiredTools.Where(t => !toolNames.Contains(t)).ToList();
                if (missing.Count > 0)
                    toolLogger.LogWarning("Agent '{AgentName}' requires tools not registered: {MissingTools}",
                        dep.AgentName, string.Join(", ", missing));
            }

            var agentOptions = new ChatClientAgentOptions
            {
                Name = options.AgentName,
                Description = options.AgentDescription,
                ChatOptions = new ChatOptions { Tools = tools },
                ChatHistoryProvider = chatHistoryProvider,
                AIContextProviders = contextProviders.Count > 0 ? contextProviders : null,
            };

            var agent = new ChatClientAgent(chatClient, agentOptions, loggerFactory, sp);

            var planningMiddleware = sp.GetRequiredService<PlanningMiddleware>();
            var builder = agent.AsBuilder();
            builder.Use(async (messages, session, runOptions, nextAsync, ct) =>
                await planningMiddleware.InvokeAsync(messages, session, runOptions, nextAsync, ct));
            builder.Use(async (messages, session, runOptions, nextAsync, ct) =>
            {
                var logger = loggerFactory.CreateLogger("AiAgentCanvas.Middleware");
                var sw = Stopwatch.StartNew();
                logger.LogInformation("Agent invoked. Messages={Count}", messages.Count());
                await nextAsync(messages, session, runOptions, ct);
                logger.LogInformation("Agent completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
            });

            return (AIAgent)builder.Build(sp);
        });

        services.AddCors(cors =>
        {
            cors.AddDefaultPolicy(policy =>
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
        });

        return services;
    }

    public static IServiceCollection AddAiAgentCanvasRag(this IServiceCollection services)
    {
        services.AddEmbeddingGenerator<string, Embedding<float>>(sp =>
        {
            var generator = sp.GetRequiredService<AIFoundryClientFactory>().CreateEmbeddingGenerator();
            return generator ?? throw new InvalidOperationException(
                "EmbeddingGenerator requires AIFoundry:EmbeddingDeploymentName to be configured in appsettings.json.");
        });

        services.AddSingleton<DocumentChunker>();
        services.AddSingleton<LlmReranker>();
        services.AddSingleton<AIContextProvider>(sp =>
            new RagContextProvider(
                sp.GetRequiredService<VectorStoreCollection<string, DocumentRecord>>(),
                sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
                sp.GetRequiredService<ILogger<RagContextProvider>>(),
                sp.GetRequiredService<LlmReranker>()));
        return services;
    }

    public static IServiceCollection AddAiAgentCanvasInterAgentCommunication(
        this IServiceCollection services,
        Func<IServiceProvider, Func<string, AgentPersonaInfo?>> personaLookupFactory,
        Func<IServiceProvider, Func<IEnumerable<AgentPersonaInfo>>> personaListAllFactory,
        string mailboxConnectionString = "Data Source=agentmailbox.db")
    {
        services.AddSingleton(new AgentMailbox(mailboxConnectionString));

        services.AddSingleton(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var toolsFactory = () => sp.GetServices<IReadOnlyList<AITool>>().SelectMany(t => t);
            var contextProvidersFactory = () => sp.GetServices<AIContextProvider>();
            var personaLookup = personaLookupFactory(sp);
            var personaListAll = personaListAllFactory(sp);

            var registry = new AgentRegistry(chatClient, toolsFactory, contextProvidersFactory,
                personaLookup, personaListAll, loggerFactory);

            registry.RegisterDefault(sp.GetRequiredService<AIAgent>());
            return registry;
        });

        services.AddSingleton<AgentRegistryToolProvider>();
        services.AddSingleton<AgentMailboxToolProvider>();
        services.AddSingleton<HandoffToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<AgentRegistryToolProvider>().GetTools());
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<AgentMailboxToolProvider>().GetTools());
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<HandoffToolProvider>().GetTools());

        return services;
    }

    public static WebApplication UseAiAgentCanvas(this WebApplication app)
    {
        app.UseCors();
        app.MapAgUiEndpoints();
        app.MapGet("/api/health", (AIFoundryClientFactory factory) =>
        {
            try
            {
                factory.CreateChatClient();
                return Results.Ok(new { status = "healthy", ai = true });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Ok(new { status = "healthy", ai = false, message = ex.Message });
            }
        });
        return app;
    }
}

public sealed class AiAgentCanvasOptions
{
    public string AgentName { get; set; } = "AiAgentCanvas";
    public string AgentDescription { get; set; } = "A multi-tool AI assistant";
    public string? SystemPrompt { get; set; }
}

public sealed class DefaultSystemPrompt
{
    public string Value { get; }
    public DefaultSystemPrompt(string value) => Value = value;
}

internal sealed class SystemPromptProvider : AIContextProvider
{
    private readonly string _systemPrompt;

    public SystemPromptProvider(string systemPrompt) => _systemPrompt = systemPrompt;

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(context.AIContext.Instructions))
            context.AIContext.Instructions = _systemPrompt;
        return new ValueTask<AIContext>(context.AIContext);
    }
}
