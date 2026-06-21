using Microsoft.Data.Sqlite;

namespace AiAgentCanvas.Scheduler;

public sealed class ScheduledTaskRecord
{
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string? CronExpression { get; set; }
    public bool IsRecurring { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class ScheduledTaskResult
{
    public string TaskId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string CompletedAt { get; set; } = string.Empty;
}

public sealed class ScheduledTaskStore : IDisposable
{
    private readonly SqliteConnection _connection;

    public ScheduledTaskStore(string connectionString)
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        Initialize();
    }

    private void Initialize()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS scheduled_tasks (
                id TEXT PRIMARY KEY,
                description TEXT NOT NULL,
                prompt TEXT NOT NULL,
                cron_expression TEXT,
                is_recurring INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
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
    }

    public void SaveTask(ScheduledTaskRecord task)
    {
        using var cmd = _connection.CreateCommand();
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
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, description, prompt, cron_expression, is_recurring, created_at FROM scheduled_tasks ORDER BY created_at DESC";

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
            });
        }
        return tasks;
    }

    public bool RemoveTask(string id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM scheduled_tasks WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    public void SaveResult(string taskId, string description, string result)
    {
        using var cmd = _connection.CreateCommand();
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
        using var cmd = _connection.CreateCommand();
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

    public void Dispose() => _connection.Dispose();
}
