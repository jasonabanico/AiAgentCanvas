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
using Microsoft.Extensions.Diagnostics.HealthChecks;
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
        services.AddSingleton<IAgentMessaging, InProcessAgentMessaging>();

        var options = new AiAgentCanvasOptions();
        configure?.Invoke(options);

        services.AddSingleton<DynamicToolRegistry>();

        var defaultPrompt = options.SystemPrompt ?? "You are a helpful AI assistant. Use the available tools to help answer questions.";
        services.AddSingleton(new DefaultSystemPrompt(defaultPrompt));

        services.AddSingleton<AIContextProvider>(new SystemPromptProvider(defaultPrompt));

        services.AddSingleton(sp =>
            new DynamicToolContextProvider(sp.GetRequiredService<DynamicToolRegistry>()));

        services.AddSingleton<ToolStatusBroker>();

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
            var toolStatusBroker = sp.GetRequiredService<ToolStatusBroker>();
            var tools = rawTools.Select(t =>
            {
                if (t is not AIFunction fn) return t;
                if (governanceWrapper is not null) fn = governanceWrapper.Wrap(fn);
                fn = new StatusEmittingFunction(fn, toolStatusBroker);
                return (AITool)fn;
            }).ToList();
            var toolLogger = loggerFactory.CreateLogger("AiAgentCanvas.ToolRegistration");
            toolLogger.LogInformation("Registered {ToolCount} tools (governance={Governed}): {ToolNames}",
                tools.Count, governanceWrapper is not null, string.Join(", ", tools.Select(t => t.Name)));

            var toolNames = new HashSet<string>(tools.Select(t => t.Name));
            var agentToolSeeds = sp.GetServices<IAgentToolsSeed>().ToDictionary(s => s.AgentName, StringComparer.OrdinalIgnoreCase);
            foreach (var seed in agentToolSeeds.Values)
            {
                var missing = seed.ToolNames.Where(t => !toolNames.Contains(t)).ToList();
                if (missing.Count > 0)
                    toolLogger.LogWarning("Agent '{AgentName}' declares tools not registered: {MissingTools}",
                        seed.AgentName, string.Join(", ", missing));
            }

            var defaultTools = agentToolSeeds.TryGetValue(options.AgentName, out var defaultSeed)
                ? tools.Where(t => defaultSeed.ToolNames.Contains(t.Name)).ToList()
                : tools;

            var agentOptions = new ChatClientAgentOptions
            {
                Name = options.AgentName,
                Description = options.AgentDescription,
                ChatOptions = new ChatOptions { Tools = defaultTools },
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

        services.AddSingleton<AIHealthCheck>();
        services.AddHostedService(sp => sp.GetRequiredService<AIHealthCheck>());
        services.AddHealthChecks().AddCheck<AIHealthCheck>("ai-agent-pipeline");

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
        services.AddSingleton<IAgentMailbox>(new SqliteAgentMailbox(mailboxConnectionString));

        services.AddSingleton(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var toolsFactory = () => sp.GetServices<IReadOnlyList<AITool>>().SelectMany(t => t);
            var contextProvidersFactory = () => sp.GetServices<AIContextProvider>();
            var personaLookup = personaLookupFactory(sp);
            var personaListAll = personaListAllFactory(sp);
            var toolSeeds = sp.GetServices<IAgentToolsSeed>().ToDictionary(s => s.AgentName, StringComparer.OrdinalIgnoreCase);

            var registry = new AgentRegistry(chatClient, toolsFactory, contextProvidersFactory,
                personaLookup, personaListAll, toolSeeds, loggerFactory);

            registry.SetDefaultAgentFactory(() => sp.GetRequiredService<AIAgent>());
            return registry;
        });
        services.AddSingleton<IAgentRegistry>(sp => sp.GetRequiredService<AgentRegistry>());
        services.AddSingleton<IAgentHandoff, InProcessAgentHandoff>();

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
        app.MapHealthChecks("/api/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            ResponseWriter = WriteHealthResponse,
        });
        return app;
    }

    private static async Task WriteHealthResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            duration_ms = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration_ms = e.Value.Duration.TotalMilliseconds,
                data = e.Value.Data,
                error = e.Value.Exception?.Message,
            }),
        };
        await context.Response.WriteAsJsonAsync(result);
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
