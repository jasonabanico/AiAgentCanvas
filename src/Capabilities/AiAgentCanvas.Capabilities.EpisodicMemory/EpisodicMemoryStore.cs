using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.EpisodicMemory;

public sealed class EpisodicMemoryStore : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly ILogger<EpisodicMemoryStore> _logger;
    private const double DecayRate = 0.05;

    public EpisodicMemoryStore(string dbPath, ILogger<EpisodicMemoryStore> logger)
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
            CREATE TABLE IF NOT EXISTS episodes (
                id TEXT PRIMARY KEY,
                agent_name TEXT NOT NULL,
                goal TEXT NOT NULL,
                summary TEXT NOT NULL,
                outcome TEXT NOT NULL DEFAULT 'unknown',
                tools_used TEXT NOT NULL DEFAULT '[]',
                turn_count INTEGER NOT NULL DEFAULT 0,
                started_at TEXT NOT NULL,
                completed_at TEXT NOT NULL,
                relevance_score REAL NOT NULL DEFAULT 1.0
            );
            CREATE INDEX IF NOT EXISTS idx_episodes_agent ON episodes(agent_name);
            CREATE INDEX IF NOT EXISTS idx_episodes_outcome ON episodes(outcome);
            """;
        cmd.ExecuteNonQuery();
    }

    public void Save(Episode episode)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO episodes (id, agent_name, goal, summary, outcome, tools_used, turn_count, started_at, completed_at, relevance_score)
            VALUES ($id, $agent, $goal, $summary, $outcome, $tools, $turns, $started, $completed, $relevance)
            """;
        cmd.Parameters.AddWithValue("$id", episode.Id);
        cmd.Parameters.AddWithValue("$agent", episode.AgentName);
        cmd.Parameters.AddWithValue("$goal", episode.Goal);
        cmd.Parameters.AddWithValue("$summary", episode.Summary);
        cmd.Parameters.AddWithValue("$outcome", episode.Outcome);
        cmd.Parameters.AddWithValue("$tools", JsonSerializer.Serialize(episode.ToolsUsed));
        cmd.Parameters.AddWithValue("$turns", episode.TurnCount);
        cmd.Parameters.AddWithValue("$started", episode.StartedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$completed", episode.CompletedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$relevance", episode.RelevanceScore);
        cmd.ExecuteNonQuery();
        _logger.LogInformation("Saved episode {Id}: {Goal} -> {Outcome}", episode.Id, episode.Goal, episode.Outcome);
    }

    public IReadOnlyList<Episode> Search(string query, string? agentName = null, int limit = 5)
    {
        using var cmd = _db.CreateCommand();
        var whereClauses = new List<string> { "relevance_score > 0.1" };
        if (agentName is not null)
        {
            whereClauses.Add("agent_name = $agent");
            cmd.Parameters.AddWithValue("$agent", agentName);
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            whereClauses.Add("(goal LIKE $q OR summary LIKE $q)");
            cmd.Parameters.AddWithValue("$q", $"%{query}%");
        }

        cmd.CommandText = $"""
            SELECT id, agent_name, goal, summary, outcome, tools_used, turn_count, started_at, completed_at, relevance_score
            FROM episodes
            WHERE {string.Join(" AND ", whereClauses)}
            ORDER BY relevance_score DESC, completed_at DESC
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$limit", limit);

        var results = new List<Episode>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(ReadEpisode(reader));
        return results;
    }

    public IReadOnlyList<Episode> GetRecent(string? agentName = null, int limit = 10)
    {
        using var cmd = _db.CreateCommand();
        if (agentName is not null)
        {
            cmd.CommandText = """
                SELECT id, agent_name, goal, summary, outcome, tools_used, turn_count, started_at, completed_at, relevance_score
                FROM episodes WHERE agent_name = $agent ORDER BY completed_at DESC LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$agent", agentName);
        }
        else
        {
            cmd.CommandText = """
                SELECT id, agent_name, goal, summary, outcome, tools_used, turn_count, started_at, completed_at, relevance_score
                FROM episodes ORDER BY completed_at DESC LIMIT $limit
                """;
        }
        cmd.Parameters.AddWithValue("$limit", limit);

        var results = new List<Episode>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(ReadEpisode(reader));
        return results;
    }

    public void ApplyDecay()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = $"UPDATE episodes SET relevance_score = relevance_score * (1.0 - {DecayRate})";
        var affected = cmd.ExecuteNonQuery();
        _logger.LogDebug("Applied decay to {Count} episodes", affected);

        using var cleanup = _db.CreateCommand();
        cleanup.CommandText = "DELETE FROM episodes WHERE relevance_score < 0.01";
        var pruned = cleanup.ExecuteNonQuery();
        if (pruned > 0)
            _logger.LogInformation("Pruned {Count} low-relevance episodes", pruned);
    }

    private static Episode ReadEpisode(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        AgentName = reader.GetString(1),
        Goal = reader.GetString(2),
        Summary = reader.GetString(3),
        Outcome = reader.GetString(4),
        ToolsUsed = JsonSerializer.Deserialize<List<string>>(reader.GetString(5)) ?? [],
        TurnCount = reader.GetInt32(6),
        StartedAt = DateTimeOffset.Parse(reader.GetString(7)),
        CompletedAt = DateTimeOffset.Parse(reader.GetString(8)),
        RelevanceScore = reader.GetDouble(9),
    };

    public void Dispose() => _db.Dispose();
}
