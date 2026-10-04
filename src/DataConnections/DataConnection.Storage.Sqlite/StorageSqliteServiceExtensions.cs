using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DataConnection.Storage.Sqlite;

public static class StorageSqliteServiceExtensions
{
    public static IServiceCollection AddSqliteScheduledTaskStore(
        this IServiceCollection services,
        string connectionString = "Data Source=scheduler.db")
    {
        services.AddSingleton<IScheduledTaskStore>(new SqliteScheduledTaskStore(connectionString));
        return services;
    }
}
