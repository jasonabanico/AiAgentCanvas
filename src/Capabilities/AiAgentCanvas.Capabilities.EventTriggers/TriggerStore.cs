using System.Globalization;
using System.Text.Json;
using AiAgentCanvas.Abstractions;
using Microsoft.Data.Sqlite;

namespace AiAgentCanvas.Capabilities.EventTriggers;

/// <summary>
/// Durable state for triggers: the trigger definitions, the queue of fired events,
/// and the cursors of pull-mode readers. Before this, all three lived in memory, so a
/// restart lost every trigger an agent had created and every event not yet handled.
/// </summary>
public sealed class TriggerStore : ICursorStore
{
    private readonly string _connectionString;

    public TriggerStore(string databasePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        InitSchema();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void InitSchema()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS triggers (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                type TEXT NOT NULL,
                cron_expression TEXT,
                watch_path TEXT,
                condition TEXT,
                agent_message TEXT NOT NULL,
                target_agent TEXT,
                target_job TEXT,
                source_connection TEXT,
                source_event_type TEXT,
                enabled INTEGER NOT NULL DEFAULT 1,
                last_fired TEXT,
                fire_count INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS trigger_events (
                id TEXT PRIMARY KEY,
                trigger_id TEXT NOT NULL,
                trigger_name TEXT NOT NULL,
                message TEXT NOT NULL,
                target_agent TEXT,
                target_job TEXT,
                metadata TEXT,
                dedupe_key TEXT,
                status TEXT NOT NULL,
                attempts INTEGER NOT NULL DEFAULT 0,
                next_attempt_at TEXT NOT NULL,
                created_at TEXT NOT NULL,
                finished_at TEXT,
                last_error TEXT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_trigger_events_dedupe
                ON trigger_events(trigger_id, dedupe_key) WHERE dedupe_key IS NOT NULL;
            CREATE INDEX IF NOT EXISTS idx_trigger_events_due ON trigger_events(status, next_attempt_at);
            CREATE TABLE IF NOT EXISTS cursors (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    // ---- trigger definitions ----

    public IReadOnlyList<EventTrigger> LoadTriggers()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, type, cron_expression, watch_path, condition, agent_message, target_agent,
                   target_job, source_connection, source_event_type, enabled, last_fired, fire_count
            FROM triggers
            """;

        var results = new List<EventTrigger>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new EventTrigger
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Type = Enum.Parse<EventTriggerType>(reader.GetString(2)),
                CronExpression = Text(reader, 3),
                WatchPath = Text(reader, 4),
                Condition = Text(reader, 5),
                AgentMessage = reader.GetString(6),
                TargetAgent = Text(reader, 7),
                TargetJob = Text(reader, 8),
                SourceConnection = Text(reader, 9),
                SourceEventType = Text(reader, 10),
                Enabled = reader.GetInt64(11) != 0,
                LastFired = reader.IsDBNull(12) ? null : ParseTime(reader.GetString(12)),
                FireCount = reader.GetInt32(13),
            });
        }
        return results;
    }

    public void SaveTrigger(EventTrigger trigger)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO triggers (id, name, type, cron_expression, watch_path, condition, agent_message,
                                  target_agent, target_job, source_connection, source_event_type, enabled,
                                  last_fired, fire_count)
            VALUES ($id, $name, $type, $cron, $path, $condition, $message, $agent, $job, $source, $sourceType,
                    $enabled, $last, $count)
            ON CONFLICT(id) DO UPDATE SET
                name = $name, type = $type, cron_expression = $cron, watch_path = $path, condition = $condition,
                agent_message = $message, target_agent = $agent, target_job = $job,
                source_connection = $source, source_event_type = $sourceType, enabled = $enabled,
                last_fired = $last, fire_count = $count
            """;
        cmd.Parameters.AddWithValue("$id", trigger.Id);
        cmd.Parameters.AddWithValue("$name", trigger.Name);
        cmd.Parameters.AddWithValue("$type", trigger.Type.ToString());
        cmd.Parameters.AddWithValue("$cron", (object?)trigger.CronExpression ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$path", (object?)trigger.WatchPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$condition", (object?)trigger.Condition ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$message", trigger.AgentMessage);
        cmd.Parameters.AddWithValue("$agent", (object?)trigger.TargetAgent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$job", (object?)trigger.TargetJob ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$source", (object?)trigger.SourceConnection ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sourceType", (object?)trigger.SourceEventType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$enabled", trigger.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$last", trigger.LastFired is { } last ? Format(last) : DBNull.Value);
        cmd.Parameters.AddWithValue("$count", trigger.FireCount);
        cmd.ExecuteNonQuery();
    }

    public bool DeleteTrigger(string id)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM triggers WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    // ---- event queue ----

    /// <summary>
    /// Stores the event unless the backlog is at capacity or the same occurrence is
    /// already stored. The duplicate check relies on the unique index, so two callers
    /// racing with the same key cannot both be accepted.
    /// </summary>
    public EnqueueOutcome Enqueue(TriggerEvent evt, int capacity)
    {
        using var connection = Open();

        using (var count = connection.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM trigger_events WHERE status IN ('Pending', 'Running')";
            if (Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture) >= capacity)
                return EnqueueOutcome.Rejected;
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO trigger_events
                (id, trigger_id, trigger_name, message, target_agent, target_job, metadata, dedupe_key, status,
                 attempts, next_attempt_at, created_at)
            VALUES ($id, $trigger, $name, $message, $agent, $job, $metadata, $dedupe, 'Pending', 0, $now, $now)
            """;
        cmd.Parameters.AddWithValue("$id", evt.Id);
        cmd.Parameters.AddWithValue("$trigger", evt.TriggerId);
        cmd.Parameters.AddWithValue("$name", evt.TriggerName);
        cmd.Parameters.AddWithValue("$message", evt.Message);
        cmd.Parameters.AddWithValue("$agent", (object?)evt.TargetAgent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$job", (object?)evt.TargetJob ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$metadata", JsonSerializer.Serialize(evt.Metadata));
        cmd.Parameters.AddWithValue("$dedupe", (object?)evt.DedupeKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));

        return cmd.ExecuteNonQuery() == 1 ? EnqueueOutcome.Accepted : EnqueueOutcome.Duplicate;
    }

    /// <summary>Takes the oldest due event and marks it running, or returns null.</summary>
    public TriggerEvent? ClaimNext(DateTimeOffset now)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();

        TriggerEvent? claimed;
        using (var select = connection.CreateCommand())
        {
            select.Transaction = tx;
            select.CommandText = $"""
                SELECT {EventColumns} FROM trigger_events
                WHERE status = 'Pending' AND next_attempt_at <= $now
                ORDER BY created_at LIMIT 1
                """;
            select.Parameters.AddWithValue("$now", Format(now));
            using var reader = select.ExecuteReader();
            claimed = reader.Read() ? ReadEvent(reader) : null;
        }

        if (claimed is null)
            return null;

        using (var update = connection.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText = "UPDATE trigger_events SET status = 'Running', attempts = attempts + 1 WHERE id = $id";
            update.Parameters.AddWithValue("$id", claimed.Id);
            update.ExecuteNonQuery();
        }

        tx.Commit();
        claimed.Attempts++;
        return claimed;
    }

    public void Complete(string id)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE trigger_events SET status = 'Succeeded', finished_at = $now, last_error = NULL WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Returns the event to the queue to run again at the given time.</summary>
    public void Retry(string id, string error, DateTimeOffset retryAt)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE trigger_events SET status = 'Pending', next_attempt_at = $at, last_error = $error WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$at", Format(retryAt));
        cmd.Parameters.AddWithValue("$error", error);
        cmd.ExecuteNonQuery();
    }

    public void MarkDead(string id, string error)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE trigger_events SET status = 'Dead', finished_at = $now, last_error = $error WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
        cmd.Parameters.AddWithValue("$error", error);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Holds a claimed event until the given time without spending an attempt, for work
    /// that was refused rather than tried, such as a run blocked by a spend limit.
    /// </summary>
    public void Defer(string id, DateTimeOffset until, string reason)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE trigger_events SET status = 'Pending', attempts = MAX(attempts - 1, 0),
                next_attempt_at = $at, last_error = $reason
            WHERE id = $id AND status = 'Running'
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$at", Format(until));
        cmd.Parameters.AddWithValue("$reason", reason);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Puts a claimed event back without spending an attempt, for a clean shutdown.</summary>
    public void Release(string id)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE trigger_events SET status = 'Pending', attempts = MAX(attempts - 1, 0),
                next_attempt_at = $now
            WHERE id = $id AND status = 'Running'
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Events left running by a process that stopped are queued again. At least once, not exactly once.</summary>
    public int RecoverInterrupted()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE trigger_events SET status = 'Pending', next_attempt_at = $now WHERE status = 'Running'";
        cmd.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
        return cmd.ExecuteNonQuery();
    }

    public TriggerQueueCounts Counts()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(SUM(status = 'Pending'), 0),
                   COALESCE(SUM(status = 'Running'), 0),
                   COALESCE(SUM(status = 'Dead'), 0)
            FROM trigger_events
            """;
        using var reader = cmd.ExecuteReader();
        reader.Read();
        return new TriggerQueueCounts(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
    }

    public IReadOnlyList<TriggerEvent> ListByStatus(TriggerEventStatus status, int limit)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {EventColumns} FROM trigger_events WHERE status = $status ORDER BY created_at DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$status", status.ToString());
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200));

        var results = new List<TriggerEvent>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(ReadEvent(reader));
        return results;
    }

    /// <summary>Gives a dead event another full set of attempts.</summary>
    public bool Requeue(string id)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE trigger_events SET status = 'Pending', attempts = 0, finished_at = NULL, next_attempt_at = $now
            WHERE id = $id AND status = 'Dead'
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>Deletes succeeded events older than the cutoff. Dead events stay until retried or removed by hand.</summary>
    public int PruneSucceeded(DateTimeOffset olderThan)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM trigger_events WHERE status = 'Succeeded' AND finished_at < $cutoff";
        cmd.Parameters.AddWithValue("$cutoff", Format(olderThan));
        return cmd.ExecuteNonQuery();
    }

    // ---- cursors ----

    public string? Get(string key)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM cursors WHERE key = $key";
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() as string;
    }

    public void Set(string key, string value)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO cursors (key, value, updated_at) VALUES ($key, $value, $now)
            ON CONFLICT(key) DO UPDATE SET value = $value, updated_at = $now
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
        cmd.ExecuteNonQuery();
    }

    private const string EventColumns =
        "id, trigger_id, trigger_name, message, target_agent, target_job, metadata, dedupe_key, attempts, created_at, last_error";

    private static TriggerEvent ReadEvent(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        TriggerId = reader.GetString(1),
        TriggerName = reader.GetString(2),
        Message = reader.GetString(3),
        TargetAgent = Text(reader, 4),
        TargetJob = Text(reader, 5),
        Metadata = reader.IsDBNull(6)
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(6)) ?? [],
        DedupeKey = Text(reader, 7),
        Attempts = reader.GetInt32(8),
        FiredAt = ParseTime(reader.GetString(9)),
        LastError = Text(reader, 10),
    };

    private static string? Text(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTime(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
