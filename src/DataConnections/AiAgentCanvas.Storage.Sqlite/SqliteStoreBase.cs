using Microsoft.Data.Sqlite;

namespace AiAgentCanvas.Storage.Sqlite;

public abstract class SqliteStoreBase
{
    protected string ConnectionString { get; }

    protected SqliteStoreBase(string connectionString)
    {
        ConnectionString = connectionString;
        EnsureCreated();
    }

    protected SqliteStoreBase(string directory, string databaseName)
    {
        Directory.CreateDirectory(directory);
        var dbPath = Path.Combine(directory, databaseName);
        ConnectionString = $"Data Source={dbPath}";
        EnsureCreated();
    }

    protected abstract void CreateSchema(SqliteConnection connection);

    protected SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    private void EnsureCreated()
    {
        using var connection = OpenConnection();
        CreateSchema(connection);
    }
}
