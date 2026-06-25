using AiAgentCanvas.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace VectorStore.Sqlite;

public static class SqliteVectorStoreExtensions
{
    public static IServiceCollection AddSqliteVectorStore(this IServiceCollection services, string connectionString = "Data Source=vectorstore.db")
    {
        services.AddSingleton<VectorStoreCollection<string, DocumentRecord>>(sp =>
        {
            var collection = new SqliteDocumentCollection(connectionString);
            collection.EnsureCollectionExistsAsync().GetAwaiter().GetResult();
            return collection;
        });
        return services;
    }

    public static IServiceCollection AddSqliteVectorStore(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration["VectorStore:ConnectionString"] ?? "Data Source=vectorstore.db";
        return services.AddSqliteVectorStore(connectionString);
    }

    public static IServiceCollection AddSqliteChatHistory(this IServiceCollection services, string connectionString = "Data Source=chathistory.db")
    {
        services.AddSingleton<ChatHistoryProvider>(sp =>
            new SqliteChatHistoryProvider(connectionString, sp.GetRequiredService<ILogger<SqliteChatHistoryProvider>>()));
        return services;
    }
}
