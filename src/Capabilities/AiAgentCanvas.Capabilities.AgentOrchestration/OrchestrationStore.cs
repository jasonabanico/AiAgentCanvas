using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace AiAgentCanvas.Capabilities.AgentOrchestration;

/// <summary>Durable record of each orchestration run, so a run waiting on a person survives a restart.</summary>
public sealed class OrchestrationStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _connectionString;

    public OrchestrationStore(string databasePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS orchestration_runs (
                id TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                spec TEXT NOT NULL,
                status TEXT NOT NULL,
                pending TEXT,
                result TEXT,
                transcript TEXT NOT NULL,
                error TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_orchestration_status ON orchestration_runs(status, updated_at);
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public void Save(OrchestrationRun run)
    {
        run.UpdatedAt = DateTimeOffset.UtcNow;

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO orchestration_runs (id, kind, spec, status, pending, result, transcript, error, created_at, updated_at)
            VALUES ($id, $kind, $spec, $status, $pending, $result, $transcript, $error, $created, $updated)
            ON CONFLICT(id) DO UPDATE SET
                status = $status, pending = $pending, result = $result, transcript = $transcript,
                error = $error, updated_at = $updated
            """;
        cmd.Parameters.AddWithValue("$id", run.Id);
        cmd.Parameters.AddWithValue("$kind", run.Spec.Kind.ToString());
        cmd.Parameters.AddWithValue("$spec", JsonSerializer.Serialize(run.Spec, Json));
        cmd.Parameters.AddWithValue("$status", run.Status.ToString());
        cmd.Parameters.AddWithValue("$pending", run.Pending is null ? DBNull.Value : JsonSerializer.Serialize(run.Pending, Json));
        cmd.Parameters.AddWithValue("$result", (object?)run.Result ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$transcript", JsonSerializer.Serialize(run.Transcript, Json));
        cmd.Parameters.AddWithValue("$error", (object?)run.Error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", Format(run.CreatedAt));
        cmd.Parameters.AddWithValue("$updated", Format(run.UpdatedAt));
        cmd.ExecuteNonQuery();
    }

    public OrchestrationRun? Get(string id)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM orchestration_runs WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return ReadAll(cmd).FirstOrDefault();
    }

    public IReadOnlyList<OrchestrationRun> List(int limit = 50, OrchestrationStatus? status = null)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM orchestration_runs {(status is null ? "" : "WHERE status = $status")} ORDER BY updated_at DESC LIMIT $limit";
        if (status is not null)
            cmd.Parameters.AddWithValue("$status", status.Value.ToString());
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        return ReadAll(cmd);
    }

    /// <summary>Marks runs that a previous process left in the running state. Their checkpoints are intact.</summary>
    public int MarkInterrupted()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE orchestration_runs SET status = 'Interrupted', updated_at = $now WHERE status = 'Running'";
        cmd.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
        return cmd.ExecuteNonQuery();
    }

    /// <summary>Deletes finished runs older than the cutoff and returns their ids, so the caller can remove checkpoints.</summary>
    public IReadOnlyList<string> PruneFinished(DateTimeOffset olderThan)
    {
        using var db = Open();
        var ids = new List<string>();

        using (var select = db.CreateCommand())
        {
            select.CommandText = "SELECT id FROM orchestration_runs WHERE status IN ('Completed','Failed','Cancelled') AND updated_at < $cutoff";
            select.Parameters.AddWithValue("$cutoff", Format(olderThan));
            using var reader = select.ExecuteReader();
            while (reader.Read())
                ids.Add(reader.GetString(0));
        }

        foreach (var id in ids)
        {
            using var delete = db.CreateCommand();
            delete.CommandText = "DELETE FROM orchestration_runs WHERE id = $id";
            delete.Parameters.AddWithValue("$id", id);
            delete.ExecuteNonQuery();
        }

        return ids;
    }

    private const string Columns = "id, spec, status, pending, result, transcript, error, created_at, updated_at";

    private static List<OrchestrationRun> ReadAll(SqliteCommand cmd)
    {
        var runs = new List<OrchestrationRun>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            runs.Add(new OrchestrationRun
            {
                Id = reader.GetString(0),
                Spec = JsonSerializer.Deserialize<OrchestrationSpec>(reader.GetString(1), Json)!,
                Status = Enum.Parse<OrchestrationStatus>(reader.GetString(2)),
                Pending = reader.IsDBNull(3) ? null : JsonSerializer.Deserialize<PendingInput>(reader.GetString(3), Json),
                Result = reader.IsDBNull(4) ? null : reader.GetString(4),
                Transcript = JsonSerializer.Deserialize<List<TranscriptEntry>>(reader.GetString(5), Json) ?? [],
                Error = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt = Parse(reader.GetString(7)),
                UpdatedAt = Parse(reader.GetString(8)),
            });
        }
        return runs;
    }

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
