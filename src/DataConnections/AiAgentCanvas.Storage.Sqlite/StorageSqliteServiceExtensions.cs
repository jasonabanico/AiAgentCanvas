using AiAgentCanvas.Capabilities.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.Storage.Sqlite;

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
