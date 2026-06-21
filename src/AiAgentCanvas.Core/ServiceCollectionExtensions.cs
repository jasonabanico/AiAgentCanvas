using System.Diagnostics;
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
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var chatHistoryProvider = sp.GetService<ChatHistoryProvider>();
            var contextProviders = sp.GetServices<AIContextProvider>().ToList();

            var tools = sp.GetServices<IReadOnlyList<AITool>>().SelectMany(t => t).ToList();
            var toolLogger = loggerFactory.CreateLogger("AiAgentCanvas.ToolRegistration");
            toolLogger.LogInformation("Registered {ToolCount} tools: {ToolNames}",
                tools.Count, string.Join(", ", tools.Select(t => t.Name)));

            var agentOptions = new ChatClientAgentOptions
            {
                Name = options.AgentName,
                Description = options.AgentDescription,
                ChatOptions = new ChatOptions { Tools = tools },
                ChatHistoryProvider = chatHistoryProvider,
                AIContextProviders = contextProviders.Count > 0 ? contextProviders : null,
            };

            var agent = new ChatClientAgent(chatClient, agentOptions, loggerFactory, sp);

            var builder = agent.AsBuilder();
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

        services.AddSingleton<AIContextProvider, RagContextProvider>();
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
