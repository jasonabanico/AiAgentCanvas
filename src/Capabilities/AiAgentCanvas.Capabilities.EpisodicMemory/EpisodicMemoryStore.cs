using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.EpisodicMemory;

public sealed class EpisodicMemoryStore : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly ILogger<EpisodicMemoryStore> _logger;
    private const double DecayRate = 0.05;

    /// <summary>
    /// Episodes scoring below this are not written. Keeping everything defeats the
    /// purpose: recall degrades as the store fills with turns nothing needed.
    /// </summary>
    public double ImportanceThreshold { get; init; } = 0.3;

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

        AddColumnIfMissing("importance", "REAL NOT NULL DEFAULT 0.5");
        AddColumnIfMissing("embedding", "BLOB");
    }

    /// <summary>
    /// Brings an existing database forward without a migration step. SQLite has no
    /// ADD COLUMN IF NOT EXISTS, so the column list is checked first.
    /// </summary>
    private void AddColumnIfMissing(string column, string definition)
    {
        using var check = _db.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('episodes') WHERE name = @name";
        check.Parameters.AddWithValue("@name", column);
        if (Convert.ToInt64(check.ExecuteScalar()) > 0)
            return;

        using var alter = _db.CreateCommand();
        alter.CommandText = $"ALTER TABLE episodes ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    /// <summary>
    /// Writes the episode when it clears <see cref="ImportanceThreshold"/>.
    /// Returns false when it was filtered out, so the caller can say so.
    /// </summary>
    public bool Save(Episode episode)
    {
        if (episode.Importance < ImportanceThreshold)
        {
            _logger.LogDebug("Skipped episode '{Goal}', importance {Importance} is below the {Threshold} threshold",
                episode.Goal, episode.Importance, ImportanceThreshold);
            return false;
        }

        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO episodes (id, agent_name, goal, summary, outcome, tools_used, turn_count, started_at, completed_at, relevance_score, importance, embedding)
            VALUES ($id, $agent, $goal, $summary, $outcome, $tools, $turns, $started, $completed, $relevance, $importance, $embedding)
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
        cmd.Parameters.AddWithValue("$importance", episode.Importance);
        cmd.Parameters.AddWithValue("$embedding", (object?)ToBlob(episode.Embedding) ?? DBNull.Value);
        cmd.ExecuteNonQuery();

        _logger.LogInformation("Saved episode {Id}: {Goal} -> {Outcome} (importance {Importance}, embedded={Embedded})",
            episode.Id, episode.Goal, episode.Outcome, episode.Importance, episode.Embedding is not null);
        return true;
    }

    /// <summary>
    /// Ranks stored episodes against a query embedding by cosine similarity,
    /// weighted by the episode's decayed relevance. Falls back to
    /// <see cref="Search"/> when nothing in the store has an embedding.
    /// </summary>
    public IReadOnlyList<Episode> SearchByEmbedding(
        ReadOnlyMemory<float> queryEmbedding,
        string? agentName = null,
        int limit = 5,
        string? keywordFallback = null)
    {
        var candidates = LoadAll(agentName).Where(e => e.Embedding is { Length: > 0 }).ToList();

        if (candidates.Count == 0)
            return Search(keywordFallback ?? string.Empty, agentName, limit);

        return candidates
            .Select(e => (Episode: e, Score: CosineSimilarity(queryEmbedding.Span, e.Embedding!) * e.RelevanceScore))
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .Select(x => x.Episode)
            .ToList();
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
            SELECT {Columns}
            FROM episodes
            WHERE {string.Join(" AND ", whereClauses)}
            ORDER BY relevance_score DESC, completed_at DESC
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$limit", limit);

        return ReadAll(cmd);
    }

    public IReadOnlyList<Episode> GetRecent(string? agentName = null, int limit = 10)
    {
        using var cmd = _db.CreateCommand();
        if (agentName is not null)
        {
            cmd.CommandText = $"SELECT {Columns} FROM episodes WHERE agent_name = $agent ORDER BY completed_at DESC LIMIT $limit";
            cmd.Parameters.AddWithValue("$agent", agentName);
        }
        else
        {
            cmd.CommandText = $"SELECT {Columns} FROM episodes ORDER BY completed_at DESC LIMIT $limit";
        }
        cmd.Parameters.AddWithValue("$limit", limit);

        return ReadAll(cmd);
    }

    public void ApplyDecay()
    {
        using var cmd = _db.CreateCommand();
        // Important episodes decay more slowly, so a hard-won lesson outlives a routine one.
        cmd.CommandText = $"UPDATE episodes SET relevance_score = relevance_score * (1.0 - {DecayRate} * (1.0 - importance))";
        var affected = cmd.ExecuteNonQuery();
        _logger.LogDebug("Applied decay to {Count} episodes", affected);

        using var cleanup = _db.CreateCommand();
        cleanup.CommandText = "DELETE FROM episodes WHERE relevance_score < 0.01";
        var pruned = cleanup.ExecuteNonQuery();
        if (pruned > 0)
            _logger.LogInformation("Pruned {Count} low-relevance episodes", pruned);
    }

    private const string Columns =
        "id, agent_name, goal, summary, outcome, tools_used, turn_count, started_at, completed_at, relevance_score, importance, embedding";

    private List<Episode> LoadAll(string? agentName)
    {
        using var cmd = _db.CreateCommand();
        if (agentName is not null)
        {
            cmd.CommandText = $"SELECT {Columns} FROM episodes WHERE agent_name = $agent AND relevance_score > 0.1";
            cmd.Parameters.AddWithValue("$agent", agentName);
        }
        else
        {
            cmd.CommandText = $"SELECT {Columns} FROM episodes WHERE relevance_score > 0.1";
        }

        return ReadAll(cmd);
    }

    private static List<Episode> ReadAll(SqliteCommand cmd)
    {
        var results = new List<Episode>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(ReadEpisode(reader));
        return results;
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
        Importance = reader.IsDBNull(10) ? 0.5 : reader.GetDouble(10),
        Embedding = reader.IsDBNull(11) ? null : FromBlob((byte[])reader.GetValue(11)),
    };

    private static byte[]? ToBlob(float[]? vector)
    {
        if (vector is null || vector.Length == 0)
            return null;

        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] FromBlob(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }

    private static double CosineSimilarity(ReadOnlySpan<float> a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0;

        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        var denominator = Math.Sqrt(magA) * Math.Sqrt(magB);
        return denominator == 0 ? 0 : dot / denominator;
    }

    public void Dispose() => _db.Dispose();
}
