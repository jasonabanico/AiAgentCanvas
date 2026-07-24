#pragma warning disable MAAI001
#pragma warning disable MEAI001

using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Orchestration.Services;
using AiAgentCanvas.Orchestration.Skills;
using System.Diagnostics;
using System.Text.Json;
using AGUI.Abstractions;
using AGUI.Server;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiAgentCanvas.Orchestration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAiAgentCanvas(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<AiAgentCanvasOptions>? configure = null)
    {
        services.AddHttpClient();

        var options = new AiAgentCanvasOptions();
        configure?.Invoke(options);

        services.AddSingleton<DynamicToolRegistry>();

        var defaultPrompt = options.SystemPrompt ?? "You are a helpful AI assistant. Use the available tools to help answer questions.";
        services.AddSingleton(new DefaultSystemPrompt(defaultPrompt));

        services.AddSingleton<AIContextProvider>(new SystemPromptProvider(defaultPrompt));

        services.AddSingleton(sp =>
        {
            var rawChatClient = sp.GetRequiredService<IChatClient>();
            var dedupeLogger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<ToolDeduplicatingChatClient>();
            var chatClient = new ToolDeduplicatingChatClient(rawChatClient, dedupeLogger);
            var contextProviders = sp.GetServices<AIContextProvider>().ToList();

            var rawTools = sp.GetServices<IReadOnlyList<AITool>>().SelectMany(t => t).ToList();
            var governanceWrapper = sp.GetService<IToolGovernanceWrapper>();
            var tools = rawTools.Select(t =>
            {
                if (t is not AIFunction fn) return t;
                if (governanceWrapper is not null) fn = governanceWrapper.Wrap(fn);
                return (AITool)fn;
            }).ToList();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
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

            var registry = sp.GetService<AgentRegistry>();
            List<AIAgent>? backgroundAgents = null;
            if (registry is not null)
            {
                backgroundAgents = registry.ListAvailableAgents()
                    .Where(n => !n.Equals("default", StringComparison.OrdinalIgnoreCase))
                    .Select(n => registry.Resolve(n))
                    .Where(a => a is not null)
                    .ToList()!;
                if (backgroundAgents.Count > 0)
                    toolLogger.LogInformation("Registered {Count} background agents: {Names}",
                        backgroundAgents.Count, string.Join(", ", backgroundAgents.Select(a => a.Name)));
            }

            AIAgent agent = chatClient.AsHarnessAgent(new HarnessAgentOptions
            {
                Name = options.AgentName,
                ChatOptions = new ChatOptions
                {
                    Instructions = defaultPrompt,
                    Tools = defaultTools,
                },
                AIContextProviders = contextProviders.Count > 0 ? contextProviders : null,
                MaxContextWindowTokens = 128_000,
                MaxOutputTokens = 16_384,
                DisableWebSearch = true,
                DisableFileMemory = true,
                DisableAgentSkillsProvider = true,
                FileAccessStore = new FileSystemAgentFileStore(
                    Path.Combine(Directory.GetCurrentDirectory(), "agent-workspace")),
                BackgroundAgents = backgroundAgents is { Count: > 0 } ? backgroundAgents : null,
                ToolApprovalAgentOptions = new ToolApprovalAgentOptions
                {
                    AutoApprovalRules = [FileAccessProvider.ReadOnlyToolsAutoApprovalRule],
                },
            });

            return agent;
        });

        services.AddSingleton<AIHealthCheck>();
        services.AddHostedService(sp => sp.GetRequiredService<AIHealthCheck>());
        services.AddHealthChecks().AddCheck<AIHealthCheck>("ai-agent-pipeline");

        services.AddAGUIServer();
        services.AddHttpContextAccessor();
        services.AddSingleton<SessionIsolationKeyProvider, HttpContextSessionIsolationKeyProvider>();
        services.AddKeyedSingleton<AgentSessionStore>(options.AgentName,
            (_, _) => new InMemoryAgentSessionStore());

        services.AddSingleton<IConfigureOptions<AGUIStreamOptions>>(sp =>
            new ConfigureOptions<AGUIStreamOptions>(streamOpts =>
            {
                foreach (var mapping in sp.GetServices<ToolStateMapping>())
                {
                    switch (mapping.Behavior)
                    {
                        case ToolStateBehavior.Snapshot:
                            streamOpts.MapResult(mapping.ToolName, ToStateSnapshot);
                            break;
                        case ToolStateBehavior.Delta:
                            streamOpts.MapResult(mapping.ToolName, ToStateDelta);
                            break;
                    }
                }

                streamOpts.MapInterrupt(content =>
                {
                    if (content is not InterruptRequestContent interrupt)
                        return null;
                    return new AGUIInterrupt
                    {
                        Id = interrupt.RequestId,
                        Reason = interrupt.Reason ?? "Tool requires approval",
                        Message = interrupt.Message,
                        ToolCallId = interrupt.ToolCallId,
                        ResponseSchema = interrupt.ResponseSchema,
                        Metadata = interrupt.Metadata,
                    };
                });
            }));

        services.AddCors(cors =>
        {
            cors.AddDefaultPolicy(policy =>
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
        });

        return services;
    }

    public static WebApplication UseAiAgentCanvas(this WebApplication app, string agentName = "AiAgentCanvas", string aguiPattern = "/api/copilotkit")
    {
        app.UseCors();
        app.MapAGUIServer(agentName, aguiPattern);
        app.MapHealthChecks("/api/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            ResponseWriter = WriteHealthResponse,
        });
        return app;
    }

    internal static readonly ActivitySource AguiActivitySource =
        new(AGUIServerInstrumentation.ActivitySourceName);

    private static IEnumerable<BaseEvent> ToStateSnapshot(FunctionResultContent result)
    {
        if (result.Result is not string json || string.IsNullOrEmpty(json))
            yield break;

        JsonElement element;
        try { element = JsonSerializer.Deserialize<JsonElement>(json); }
        catch { yield break; }

        yield return new StateSnapshotEvent { Snapshot = element };
    }

    private static IEnumerable<BaseEvent> ToStateDelta(FunctionResultContent result)
    {
        if (result.Result is not string json || string.IsNullOrEmpty(json))
            yield break;

        JsonElement element;
        try { element = JsonSerializer.Deserialize<JsonElement>(json); }
        catch { yield break; }

        yield return new StateDeltaEvent { Delta = element };
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
