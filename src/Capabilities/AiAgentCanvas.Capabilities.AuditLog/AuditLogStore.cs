using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.AuditLog;

public sealed class AuditLogStore : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly ILogger<AuditLogStore> _logger;
    private static readonly HashSet<string> SensitiveParamNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "secret", "token", "key", "credential", "apikey", "api_key",
    };

    public AuditLogStore(string dbPath, ILogger<AuditLogStore> logger)
    {
        _logger = logger;
        _db = new SqliteConnection($"Data Source={dbPath}");
        _db.Open();
        InitSchema();
    }

    private void InitSchema()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS audit_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                event_type TEXT NOT NULL,
                agent_name TEXT NOT NULL,
                tool_name TEXT,
                parameters TEXT,
                result_status TEXT,
                details TEXT,
                tokens_used INTEGER,
                duration_ms INTEGER NOT NULL DEFAULT 0,
                timestamp TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_audit_timestamp ON audit_log(timestamp DESC);
            CREATE INDEX IF NOT EXISTS idx_audit_agent ON audit_log(agent_name);
            CREATE INDEX IF NOT EXISTS idx_audit_event_type ON audit_log(event_type);
            """;
        cmd.ExecuteNonQuery();
    }

    public void Record(AuditEntry entry)
    {
        entry.Parameters = RedactSensitiveValues(entry.Parameters);

        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO audit_log (event_type, agent_name, tool_name, parameters, result_status, details, tokens_used, duration_ms, timestamp)
            VALUES ($type, $agent, $tool, $params, $status, $details, $tokens, $duration, $ts)
            """;
        cmd.Parameters.AddWithValue("$type", entry.EventType);
        cmd.Parameters.AddWithValue("$agent", entry.AgentName);
        cmd.Parameters.AddWithValue("$tool", (object?)entry.ToolName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$params", (object?)entry.Parameters ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", (object?)entry.ResultStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$details", (object?)entry.Details ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tokens", (object?)entry.TokensUsed ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$duration", entry.DurationMs);
        cmd.Parameters.AddWithValue("$ts", entry.Timestamp.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<AuditEntry> Query(
        string? eventType = null,
        string? agentName = null,
        string? toolName = null,
        DateTimeOffset? since = null,
        int limit = 20)
    {
        var clauses = new List<string>();
        using var cmd = _db.CreateCommand();

        if (eventType is not null)
        {
            clauses.Add("event_type = $type");
            cmd.Parameters.AddWithValue("$type", eventType);
        }
        if (agentName is not null)
        {
            clauses.Add("agent_name = $agent");
            cmd.Parameters.AddWithValue("$agent", agentName);
        }
        if (toolName is not null)
        {
            clauses.Add("tool_name = $tool");
            cmd.Parameters.AddWithValue("$tool", toolName);
        }
        if (since is not null)
        {
            clauses.Add("timestamp >= $since");
            cmd.Parameters.AddWithValue("$since", since.Value.ToString("o"));
        }

        var where = clauses.Count > 0 ? $"WHERE {string.Join(" AND ", clauses)}" : "";
        cmd.CommandText = $"""
            SELECT id, event_type, agent_name, tool_name, parameters, result_status, details, tokens_used, duration_ms, timestamp
            FROM audit_log {where}
            ORDER BY timestamp DESC
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$limit", limit);

        var results = new List<AuditEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new AuditEntry
            {
                Id = reader.GetInt64(0),
                EventType = reader.GetString(1),
                AgentName = reader.GetString(2),
                ToolName = reader.IsDBNull(3) ? null : reader.GetString(3),
                Parameters = reader.IsDBNull(4) ? null : reader.GetString(4),
                ResultStatus = reader.IsDBNull(5) ? null : reader.GetString(5),
                Details = reader.IsDBNull(6) ? null : reader.GetString(6),
                TokensUsed = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                DurationMs = reader.GetInt64(8),
                Timestamp = DateTimeOffset.Parse(reader.GetString(9)),
            });
        }
        return results;
    }

    public (int TotalEvents, int ToolCalls, int Errors, int Handoffs) GetStats(DateTimeOffset? since = null)
    {
        using var cmd = _db.CreateCommand();
        var timeFilter = "";
        if (since is not null)
        {
            timeFilter = "WHERE timestamp >= $since";
            cmd.Parameters.AddWithValue("$since", since.Value.ToString("o"));
        }

        cmd.CommandText = $"""
            SELECT
                COUNT(*) as total,
                SUM(CASE WHEN event_type = 'tool_call' THEN 1 ELSE 0 END) as tools,
                SUM(CASE WHEN event_type = 'error' THEN 1 ELSE 0 END) as errors,
                SUM(CASE WHEN event_type = 'agent_handoff' THEN 1 ELSE 0 END) as handoffs
            FROM audit_log {timeFilter}
            """;

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
            return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
        return (0, 0, 0, 0);
    }

    private static string? RedactSensitiveValues(string? parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters))
            return parameters;

        foreach (var name in SensitiveParamNames)
        {
            var idx = parameters.IndexOf(name, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                parameters = parameters[..idx] + name + ":\"[REDACTED]\"" +
                    (parameters.IndexOf(',', idx) is var comma and >= 0 ? parameters[comma..] : "");
        }
        return parameters;
    }

    public void Dispose() => _db.Dispose();
}
