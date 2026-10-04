using System.Globalization;
using System.Text.Json;
using AiAgentCanvas.Abstractions;
using Microsoft.Data.Sqlite;

namespace AiAgentCanvas.Capabilities.RunLedger;

public sealed class SqliteRunLedger : IRunLedger
{
    private readonly string _connectionString;
    private readonly int _maxTextChars;

    public SqliteRunLedger(string databasePath, int maxTextChars = 4000)
    {
        _maxTextChars = Math.Max(200, maxTextChars);

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
            CREATE TABLE IF NOT EXISTS runs (
                id TEXT PRIMARY KEY,
                parent_id TEXT,
                source TEXT NOT NULL,
                agent_name TEXT NOT NULL,
                trigger_id TEXT,
                task_id TEXT,
                started_at TEXT NOT NULL,
                ended_at TEXT,
                status TEXT NOT NULL,
                input TEXT,
                output TEXT,
                error TEXT,
                input_tokens INTEGER NOT NULL DEFAULT 0,
                output_tokens INTEGER NOT NULL DEFAULT 0,
                model_calls INTEGER NOT NULL DEFAULT 0,
                estimated_cost REAL NOT NULL DEFAULT 0,
                tool_call_count INTEGER NOT NULL DEFAULT 0,
                termination TEXT,
                tool_calls TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_runs_started ON runs(started_at DESC);
            CREATE INDEX IF NOT EXISTS idx_runs_agent ON runs(agent_name, started_at);
            CREATE INDEX IF NOT EXISTS idx_runs_status ON runs(status);
            CREATE INDEX IF NOT EXISTS idx_runs_trigger ON runs(trigger_id, started_at);
            CREATE INDEX IF NOT EXISTS idx_runs_parent ON runs(parent_id);
            """;
        cmd.ExecuteNonQuery();
    }

    public void Start(RunRecord run)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO runs (id, parent_id, source, agent_name, trigger_id, task_id, started_at, status, input)
            VALUES ($id, $parent, $source, $agent, $trigger, $task, $started, $status, $input)
            """;
        cmd.Parameters.AddWithValue("$id", run.Id);
        cmd.Parameters.AddWithValue("$parent", (object?)run.ParentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$source", run.Source.ToString());
        cmd.Parameters.AddWithValue("$agent", run.AgentName);
        cmd.Parameters.AddWithValue("$trigger", (object?)run.TriggerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$task", (object?)run.TaskId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$started", Format(run.StartedAt));
        cmd.Parameters.AddWithValue("$status", RunStatus.Running.ToString());
        cmd.Parameters.AddWithValue("$input", (object?)Truncate(run.Input) ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Finish(string runId, RunStatus status, string? output, string? error, RunUsage usage, DateTimeOffset endedAt)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE runs SET
                ended_at = $ended, status = $status, output = $output, error = $error,
                input_tokens = $in, output_tokens = $out, model_calls = $calls,
                estimated_cost = $cost, tool_call_count = $toolCount,
                termination = $termination, tool_calls = $tools
            WHERE id = $id
            """;
        cmd.Parameters.AddWithValue("$id", runId);
        cmd.Parameters.AddWithValue("$ended", Format(endedAt));
        cmd.Parameters.AddWithValue("$status", status.ToString());
        cmd.Parameters.AddWithValue("$output", (object?)Truncate(output) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$error", (object?)Truncate(error) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$in", usage.InputTokens);
        cmd.Parameters.AddWithValue("$out", usage.OutputTokens);
        cmd.Parameters.AddWithValue("$calls", usage.ModelCalls);
        cmd.Parameters.AddWithValue("$cost", usage.EstimatedCost);
        cmd.Parameters.AddWithValue("$toolCount", usage.ToolCallCount);
        cmd.Parameters.AddWithValue("$termination", (object?)usage.Termination ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tools", JsonSerializer.Serialize(usage.ToolCalls));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<RunRecord> Recent(RunQuery query)
    {
        var clauses = new List<string>();
        using var connection = Open();
        using var cmd = connection.CreateCommand();

        if (query.Since is { } since)
        {
            clauses.Add("started_at >= $since");
            cmd.Parameters.AddWithValue("$since", Format(since));
        }
        if (query.AgentName is not null)
        {
            clauses.Add("agent_name = $agent");
            cmd.Parameters.AddWithValue("$agent", query.AgentName);
        }
        if (query.Status is { } status)
        {
            clauses.Add("status = $status");
            cmd.Parameters.AddWithValue("$status", status.ToString());
        }
        if (query.Source is { } source)
        {
            clauses.Add("source = $source");
            cmd.Parameters.AddWithValue("$source", source.ToString());
        }
        if (query.TriggerId is not null)
        {
            clauses.Add("trigger_id = $trigger");
            cmd.Parameters.AddWithValue("$trigger", query.TriggerId);
        }
        if (query.TopLevelOnly)
            clauses.Add("parent_id IS NULL");

        var where = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : string.Empty;
        cmd.CommandText = $"""
            SELECT {Columns} FROM runs {where}
            ORDER BY started_at DESC
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(query.Limit, 1, 500));

        return ReadAll(cmd);
    }

    public RunRecord? Get(string runId)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM runs WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", runId);
        return ReadAll(cmd).FirstOrDefault();
    }

    public IReadOnlyList<RunRecord> Children(string parentId)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM runs WHERE parent_id = $parent ORDER BY started_at";
        cmd.Parameters.AddWithValue("$parent", parentId);
        return ReadAll(cmd);
    }

    public RunTotals Totals(DateTimeOffset since, string? agentName = null, string? triggerId = null)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();

        var clauses = new List<string> { "parent_id IS NULL", "started_at >= $since" };
        cmd.Parameters.AddWithValue("$since", Format(since));
        if (agentName is not null)
        {
            clauses.Add("agent_name = $agent");
            cmd.Parameters.AddWithValue("$agent", agentName);
        }
        if (triggerId is not null)
        {
            clauses.Add("trigger_id = $trigger");
            cmd.Parameters.AddWithValue("$trigger", triggerId);
        }

        cmd.CommandText = $"""
            SELECT COUNT(*),
                   COALESCE(SUM(CASE WHEN status = 'Failed' THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(input_tokens), 0),
                   COALESCE(SUM(output_tokens), 0),
                   COALESCE(SUM(estimated_cost), 0)
            FROM runs WHERE {string.Join(" AND ", clauses)}
            """;

        using var reader = cmd.ExecuteReader();
        reader.Read();
        return new RunTotals(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetDouble(4));
    }

    public int Prune(DateTimeOffset olderThan)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM runs WHERE ended_at IS NOT NULL AND ended_at < $cutoff";
        cmd.Parameters.AddWithValue("$cutoff", Format(olderThan));
        return cmd.ExecuteNonQuery();
    }

    public int MarkAbandoned()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE runs SET status = $abandoned, ended_at = $now,
                error = COALESCE(error, 'the process stopped before this run finished')
            WHERE status = $running
            """;
        cmd.Parameters.AddWithValue("$abandoned", RunStatus.Abandoned.ToString());
        cmd.Parameters.AddWithValue("$running", RunStatus.Running.ToString());
        cmd.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
        return cmd.ExecuteNonQuery();
    }

    private const string Columns =
        "id, parent_id, source, agent_name, trigger_id, task_id, started_at, ended_at, status, input, output, error, " +
        "input_tokens, output_tokens, model_calls, estimated_cost, tool_call_count, termination, tool_calls";

    private static List<RunRecord> ReadAll(SqliteCommand cmd)
    {
        var results = new List<RunRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new RunRecord
            {
                Id = reader.GetString(0),
                ParentId = Nullable(reader, 1),
                Source = Enum.Parse<RunSource>(reader.GetString(2)),
                AgentName = reader.GetString(3),
                TriggerId = Nullable(reader, 4),
                TaskId = Nullable(reader, 5),
                StartedAt = Parse(reader.GetString(6)),
                EndedAt = reader.IsDBNull(7) ? null : Parse(reader.GetString(7)),
                Status = Enum.Parse<RunStatus>(reader.GetString(8)),
                Input = Nullable(reader, 9),
                Output = Nullable(reader, 10),
                Error = Nullable(reader, 11),
                InputTokens = reader.GetInt64(12),
                OutputTokens = reader.GetInt64(13),
                ModelCalls = reader.GetInt32(14),
                EstimatedCost = reader.GetDouble(15),
                ToolCallCount = reader.GetInt32(16),
                Termination = Nullable(reader, 17),
                ToolCalls = reader.IsDBNull(18)
                    ? []
                    : JsonSerializer.Deserialize<List<ToolCallRecord>>(reader.GetString(18)) ?? [],
            });
        }
        return results;
    }

    private static string? Nullable(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private string? Truncate(string? value)
    {
        if (value is null || value.Length <= _maxTextChars)
            return value;
        return value[.._maxTextChars] + "...[truncated]";
    }
}
