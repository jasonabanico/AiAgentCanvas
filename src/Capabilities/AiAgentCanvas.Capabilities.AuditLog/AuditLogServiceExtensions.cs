using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.AuditLog;

public static class AuditLogServiceExtensions
{
    public static IServiceCollection AddAiAgentCanvasAuditLog(
        this IServiceCollection services,
        string? dbPath = null)
    {
        var path = dbPath ?? Path.Combine(Directory.GetCurrentDirectory(), "audit-log.db");

        services.AddSingleton(sp =>
            new AuditLogStore(path, sp.GetRequiredService<ILogger<AuditLogStore>>()));

        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            AuditLogToolProvider.CreateTools(sp.GetRequiredService<AuditLogStore>()));

        services.AddSingleton<IAuditingChatClientFactory>(sp =>
            new AuditingChatClientFactory(
                sp.GetRequiredService<AuditLogStore>(),
                sp.GetRequiredService<ILoggerFactory>()));

        return services;
    }
}

internal sealed class AuditingChatClientFactory : IAuditingChatClientFactory
{
    private readonly AuditLogStore _store;
    private readonly ILoggerFactory _loggerFactory;

    public AuditingChatClientFactory(AuditLogStore store, ILoggerFactory loggerFactory)
    {
        _store = store;
        _loggerFactory = loggerFactory;
    }

    public IChatClient Wrap(IChatClient inner)
    {
        return new AuditingChatClient(inner, _store, logger: _loggerFactory.CreateLogger<AuditingChatClient>());
    }
}
