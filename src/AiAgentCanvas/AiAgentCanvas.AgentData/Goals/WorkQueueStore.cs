using Microsoft.Data.Sqlite;

namespace AiAgentCanvas.AgentData.Goals;

public sealed class WorkItem
{
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Priority { get; set; } = "medium";
    public string Status { get; set; } = "pending";
    public string? AssignedAgent { get; set; }
    public string? GoalName { get; set; }
    public string? Result { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string? ClaimedAt { get; set; }
    public string? CompletedAt { get; set; }
}

public sealed class WorkQueueStore : IDisposable
{
    private readonly SqliteConnection _connection;

    public WorkQueueStore(string connectionString)
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        Initialize();
    }

    private void Initialize()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS work_queue (
                id TEXT PRIMARY KEY,
                description TEXT NOT NULL,
                priority TEXT NOT NULL DEFAULT 'medium',
                status TEXT NOT NULL DEFAULT 'pending',
                assigned_agent TEXT,
                goal_name TEXT,
                result TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                claimed_at TEXT,
                completed_at TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_work_queue_status ON work_queue(status);
            CREATE INDEX IF NOT EXISTS idx_work_queue_priority ON work_queue(priority);
            """;
        cmd.ExecuteNonQuery();
    }

    public string Submit(string description, string priority, string? assignedAgent, string? goalName)
    {
        var id = $"wq-{Guid.NewGuid():N}"[..14];
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO work_queue (id, description, priority, assigned_agent, goal_name)
            VALUES (@id, @desc, @priority, @agent, @goal)
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@desc", description);
        cmd.Parameters.AddWithValue("@priority", priority);
        cmd.Parameters.AddWithValue("@agent", (object?)assignedAgent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@goal", (object?)goalName ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        return id;
    }

    public WorkItem? ClaimNext(string? agentName = null)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE work_queue
            SET status = 'claimed', claimed_at = datetime('now')
            WHERE id = (
                SELECT id FROM work_queue
                WHERE status = 'pending'
                AND (@agent IS NULL OR assigned_agent IS NULL OR assigned_agent = @agent)
                ORDER BY
                    CASE priority
                        WHEN 'critical' THEN 0
                        WHEN 'high' THEN 1
                        WHEN 'medium' THEN 2
                        WHEN 'low' THEN 3
                        ELSE 4
                    END,
                    created_at ASC
                LIMIT 1
            )
            RETURNING id, description, priority, status, assigned_agent, goal_name, result, created_at, claimed_at, completed_at
            """;
        cmd.Parameters.AddWithValue("@agent", (object?)agentName ?? DBNull.Value);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadItem(reader) : null;
    }

    public bool Complete(string id, string result)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE work_queue
            SET status = 'completed', result = @result, completed_at = datetime('now')
            WHERE id = @id AND status = 'claimed'
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@result", result);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool Fail(string id, string error)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE work_queue
            SET status = 'failed', result = @error, completed_at = datetime('now')
            WHERE id = @id AND status = 'claimed'
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@error", error);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool Cancel(string id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE work_queue
            SET status = 'cancelled', completed_at = datetime('now')
            WHERE id = @id AND status IN ('pending', 'claimed')
            """;
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    public List<WorkItem> List(string? statusFilter = null, int limit = 50)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = statusFilter is not null
            ? "SELECT id, description, priority, status, assigned_agent, goal_name, result, created_at, claimed_at, completed_at FROM work_queue WHERE status = @status ORDER BY created_at DESC LIMIT @limit"
            : "SELECT id, description, priority, status, assigned_agent, goal_name, result, created_at, claimed_at, completed_at FROM work_queue ORDER BY created_at DESC LIMIT @limit";

        if (statusFilter is not null)
            cmd.Parameters.AddWithValue("@status", statusFilter);
        cmd.Parameters.AddWithValue("@limit", limit);

        var items = new List<WorkItem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            items.Add(ReadItem(reader));
        return items;
    }

    public int PendingCount()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM work_queue WHERE status = 'pending'";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static WorkItem ReadItem(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Description = reader.GetString(1),
        Priority = reader.GetString(2),
        Status = reader.GetString(3),
        AssignedAgent = reader.IsDBNull(4) ? null : reader.GetString(4),
        GoalName = reader.IsDBNull(5) ? null : reader.GetString(5),
        Result = reader.IsDBNull(6) ? null : reader.GetString(6),
        CreatedAt = reader.GetString(7),
        ClaimedAt = reader.IsDBNull(8) ? null : reader.GetString(8),
        CompletedAt = reader.IsDBNull(9) ? null : reader.GetString(9),
    };

    public void Dispose() => _connection.Dispose();
}
