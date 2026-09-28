using AiAgentCanvas.Capabilities.Scheduling;
using Microsoft.Data.Sqlite;

namespace AiAgentCanvas.Storage.Sqlite;

public sealed class SqliteScheduledTaskStore : SqliteStoreBase, IScheduledTaskStore
{
    public SqliteScheduledTaskStore(string connectionString)
        : base(connectionString) { }

    protected override void CreateSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS scheduled_tasks (
                id TEXT PRIMARY KEY,
                description TEXT NOT NULL,
                prompt TEXT NOT NULL,
                cron_expression TEXT,
                is_recurring INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                last_run_at TEXT,
                last_error TEXT
            );
            CREATE TABLE IF NOT EXISTS scheduled_task_results (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                task_id TEXT NOT NULL,
                description TEXT NOT NULL,
                result TEXT NOT NULL,
                completed_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
            """;
        cmd.ExecuteNonQuery();

        AddColumnIfMissing(connection, "scheduled_tasks", "last_run_at", "TEXT");
        AddColumnIfMissing(connection, "scheduled_tasks", "last_error", "TEXT");
    }

    /// <summary>
    /// Brings an existing database forward without a migration step. SQLite has no
    /// ADD COLUMN IF NOT EXISTS, so the column list is checked first.
    /// </summary>
    private static void AddColumnIfMissing(SqliteConnection connection, string table, string column, string type)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = @name";
        check.Parameters.AddWithValue("@name", column);
        if (Convert.ToInt64(check.ExecuteScalar()) > 0)
            return;

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
        alter.ExecuteNonQuery();
    }

    public void SaveTask(ScheduledTaskRecord task)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO scheduled_tasks (id, description, prompt, cron_expression, is_recurring)
            VALUES (@id, @desc, @prompt, @cron, @recurring)
            """;
        cmd.Parameters.AddWithValue("@id", task.Id);
        cmd.Parameters.AddWithValue("@desc", task.Description);
        cmd.Parameters.AddWithValue("@prompt", task.Prompt);
        cmd.Parameters.AddWithValue("@cron", (object?)task.CronExpression ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@recurring", task.IsRecurring ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public List<ScheduledTaskRecord> ListTasks()
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, description, prompt, cron_expression, is_recurring, created_at, last_run_at, last_error
            FROM scheduled_tasks ORDER BY created_at DESC
            """;

        var tasks = new List<ScheduledTaskRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            tasks.Add(new ScheduledTaskRecord
            {
                Id = reader.GetString(0),
                Description = reader.GetString(1),
                Prompt = reader.GetString(2),
                CronExpression = reader.IsDBNull(3) ? null : reader.GetString(3),
                IsRecurring = reader.GetInt32(4) == 1,
                CreatedAt = reader.GetString(5),
                LastRunAt = reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
                LastError = reader.IsDBNull(7) ? null : reader.GetString(7),
            });
        }
        return tasks;
    }

    public bool RemoveTask(string id)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM scheduled_tasks WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    public void SaveResult(string taskId, string description, string result)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO scheduled_task_results (task_id, description, result)
            VALUES (@tid, @desc, @result)
            """;
        cmd.Parameters.AddWithValue("@tid", taskId);
        cmd.Parameters.AddWithValue("@desc", description);
        cmd.Parameters.AddWithValue("@result", result);
        cmd.ExecuteNonQuery();
    }

    public List<ScheduledTaskResult> GetResults(int limit = 10)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT task_id, description, result, completed_at FROM scheduled_task_results ORDER BY completed_at DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<ScheduledTaskResult>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new ScheduledTaskResult
            {
                TaskId = reader.GetString(0),
                Description = reader.GetString(1),
                Result = reader.GetString(2),
                CompletedAt = reader.GetString(3),
            });
        }
        return results;
    }

    public void MarkRun(string id, DateTimeOffset runAt, string? error = null)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE scheduled_tasks SET last_run_at = @at, last_error = @err WHERE id = @id";
        cmd.Parameters.AddWithValue("@at", runAt.ToString("o"));
        cmd.Parameters.AddWithValue("@err", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() { }
}
