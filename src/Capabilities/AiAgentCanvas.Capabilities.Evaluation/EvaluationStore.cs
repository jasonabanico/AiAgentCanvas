using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.Evaluation;

public sealed class EvaluationStore : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly ILogger<EvaluationStore> _logger;

    public EvaluationStore(string dbPath, ILogger<EvaluationStore> logger)
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
            CREATE TABLE IF NOT EXISTS eval_cases (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE,
                category TEXT NOT NULL,
                input TEXT NOT NULL,
                expected_criteria TEXT NOT NULL,
                tags TEXT,
                created_at TEXT NOT NULL,
                expected_tools TEXT,
                optimal_steps INTEGER
            );
            CREATE TABLE IF NOT EXISTS eval_results (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                eval_case_id INTEGER NOT NULL,
                eval_case_name TEXT NOT NULL,
                actual_output TEXT NOT NULL,
                score REAL NOT NULL,
                passed INTEGER NOT NULL,
                rationale TEXT,
                duration_ms INTEGER NOT NULL DEFAULT 0,
                run_at TEXT NOT NULL,
                tool_calls INTEGER NOT NULL DEFAULT 0,
                tools_used TEXT,
                tool_use_accuracy REAL,
                trajectory_efficiency REAL,
                independent_judge INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_eval_cases_category ON eval_cases(category);
            CREATE INDEX IF NOT EXISTS idx_eval_results_case ON eval_results(eval_case_id);
            CREATE INDEX IF NOT EXISTS idx_eval_results_run_at ON eval_results(run_at DESC);
            """;
        cmd.ExecuteNonQuery();

        AddColumnIfMissing("eval_cases", "expected_tools", "TEXT");
        AddColumnIfMissing("eval_cases", "optimal_steps", "INTEGER");
        AddColumnIfMissing("eval_results", "tool_calls", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing("eval_results", "tools_used", "TEXT");
        AddColumnIfMissing("eval_results", "tool_use_accuracy", "REAL");
        AddColumnIfMissing("eval_results", "trajectory_efficiency", "REAL");
        AddColumnIfMissing("eval_results", "independent_judge", "INTEGER NOT NULL DEFAULT 0");
    }

    /// <summary>
    /// Brings an existing database forward without a migration step. SQLite has no
    /// ADD COLUMN IF NOT EXISTS, so the column list is checked first.
    /// </summary>
    private void AddColumnIfMissing(string table, string column, string definition)
    {
        using var check = _db.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = @name";
        check.Parameters.AddWithValue("@name", column);
        if (Convert.ToInt64(check.ExecuteScalar()) > 0)
            return;

        using var alter = _db.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    public long AddCase(EvalCase evalCase)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO eval_cases (name, category, input, expected_criteria, tags, created_at, expected_tools, optimal_steps)
            VALUES ($name, $category, $input, $criteria, $tags, $created, $expectedTools, $optimalSteps);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", evalCase.Name);
        cmd.Parameters.AddWithValue("$category", evalCase.Category);
        cmd.Parameters.AddWithValue("$input", evalCase.Input);
        cmd.Parameters.AddWithValue("$criteria", evalCase.ExpectedCriteria);
        cmd.Parameters.AddWithValue("$tags", (object?)evalCase.Tags ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", evalCase.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$expectedTools", (object?)evalCase.ExpectedTools ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$optimalSteps", (object?)evalCase.OptimalSteps ?? DBNull.Value);

        var id = (long)cmd.ExecuteScalar()!;
        _logger.LogInformation("Added eval case '{Name}' ({Category}) with id {Id}", evalCase.Name, evalCase.Category, id);
        return id;
    }

    public EvalCase? GetCaseByName(string name)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, category, input, expected_criteria, tags, created_at, expected_tools, optimal_steps
            FROM eval_cases WHERE name = $name
            """;
        cmd.Parameters.AddWithValue("$name", name);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadCase(reader) : null;
    }

    public IReadOnlyList<EvalCase> ListCases(string? category = null)
    {
        using var cmd = _db.CreateCommand();
        var where = category is not null ? "WHERE category = $category" : "";
        cmd.CommandText = $"""
            SELECT id, name, category, input, expected_criteria, tags, created_at, expected_tools, optimal_steps
            FROM eval_cases {where}
            ORDER BY created_at DESC
            """;
        if (category is not null)
            cmd.Parameters.AddWithValue("$category", category);

        var results = new List<EvalCase>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(ReadCase(reader));
        return results;
    }

    public void RecordResult(EvalResult result)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO eval_results (eval_case_id, eval_case_name, actual_output, score, passed, rationale, duration_ms, run_at,
                                      tool_calls, tools_used, tool_use_accuracy, trajectory_efficiency, independent_judge)
            VALUES ($caseId, $caseName, $output, $score, $passed, $rationale, $duration, $ranAt,
                    $toolCalls, $toolsUsed, $toolAccuracy, $efficiency, $independent)
            """;
        cmd.Parameters.AddWithValue("$caseId", result.EvalCaseId);
        cmd.Parameters.AddWithValue("$caseName", result.EvalCaseName);
        cmd.Parameters.AddWithValue("$output", result.ActualOutput);
        cmd.Parameters.AddWithValue("$score", result.Score);
        cmd.Parameters.AddWithValue("$passed", result.Passed ? 1 : 0);
        cmd.Parameters.AddWithValue("$rationale", (object?)result.Rationale ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$duration", result.DurationMs);
        cmd.Parameters.AddWithValue("$ranAt", result.RunAt.ToString("o"));
        cmd.Parameters.AddWithValue("$toolCalls", result.ToolCalls);
        cmd.Parameters.AddWithValue("$toolsUsed", result.ToolsUsed);
        cmd.Parameters.AddWithValue("$toolAccuracy", (object?)result.ToolUseAccuracy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$efficiency", (object?)result.TrajectoryEfficiency ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$independent", result.IndependentJudge ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<EvalResult> QueryResults(
        long? evalCaseId = null,
        string? category = null,
        int limit = 20)
    {
        var clauses = new List<string>();
        using var cmd = _db.CreateCommand();

        var from = "eval_results r";
        if (category is not null)
        {
            from = "eval_results r JOIN eval_cases c ON c.id = r.eval_case_id";
            clauses.Add("c.category = $category");
            cmd.Parameters.AddWithValue("$category", category);
        }
        if (evalCaseId is not null)
        {
            clauses.Add("r.eval_case_id = $caseId");
            cmd.Parameters.AddWithValue("$caseId", evalCaseId.Value);
        }

        var where = clauses.Count > 0 ? $"WHERE {string.Join(" AND ", clauses)}" : "";
        cmd.CommandText = $"""
            SELECT r.id, r.eval_case_id, r.eval_case_name, r.actual_output, r.score, r.passed, r.rationale, r.duration_ms, r.run_at,
                   r.tool_calls, r.tools_used, r.tool_use_accuracy, r.trajectory_efficiency, r.independent_judge
            FROM {from} {where}
            ORDER BY r.run_at DESC
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$limit", limit);

        var results = new List<EvalResult>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new EvalResult
            {
                Id = reader.GetInt64(0),
                EvalCaseId = reader.GetInt64(1),
                EvalCaseName = reader.GetString(2),
                ActualOutput = reader.GetString(3),
                Score = reader.GetDouble(4),
                Passed = reader.GetInt64(5) != 0,
                Rationale = reader.IsDBNull(6) ? "" : reader.GetString(6),
                DurationMs = reader.GetInt64(7),
                RunAt = DateTimeOffset.Parse(reader.GetString(8)),
                ToolCalls = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                ToolsUsed = reader.IsDBNull(10) ? "" : reader.GetString(10),
                ToolUseAccuracy = reader.IsDBNull(11) ? null : reader.GetDouble(11),
                TrajectoryEfficiency = reader.IsDBNull(12) ? null : reader.GetDouble(12),
                IndependentJudge = !reader.IsDBNull(13) && reader.GetInt64(13) != 0,
            });
        }
        return results;
    }

    public EvalStats GetStats(string? category = null, DateTimeOffset? since = null)
    {
        using var cmd = _db.CreateCommand();
        var clauses = new List<string>();

        var from = "eval_results r";
        if (category is not null)
        {
            from = "eval_results r JOIN eval_cases c ON c.id = r.eval_case_id";
            clauses.Add("c.category = $category");
            cmd.Parameters.AddWithValue("$category", category);
        }
        if (since is not null)
        {
            clauses.Add("r.run_at >= $since");
            cmd.Parameters.AddWithValue("$since", since.Value.ToString("o"));
        }

        var where = clauses.Count > 0 ? $"WHERE {string.Join(" AND ", clauses)}" : "";
        cmd.CommandText = $"""
            SELECT COUNT(*), SUM(CASE WHEN r.passed = 1 THEN 1 ELSE 0 END), AVG(r.score),
                   AVG(r.tool_use_accuracy), AVG(r.trajectory_efficiency)
            FROM {from} {where}
            """;

        using var reader = cmd.ExecuteReader();
        if (reader.Read() && !reader.IsDBNull(0))
        {
            return new EvalStats
            {
                TotalRuns = reader.GetInt32(0),
                Passed = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                AverageScore = reader.IsDBNull(2) ? 0.0 : reader.GetDouble(2),
                AverageToolUseAccuracy = reader.IsDBNull(3) ? null : reader.GetDouble(3),
                AverageTrajectoryEfficiency = reader.IsDBNull(4) ? null : reader.GetDouble(4),
            };
        }
        return new EvalStats();
    }

    private static EvalCase ReadCase(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Name = reader.GetString(1),
        Category = reader.GetString(2),
        Input = reader.GetString(3),
        ExpectedCriteria = reader.GetString(4),
        Tags = reader.IsDBNull(5) ? null : reader.GetString(5),
        CreatedAt = DateTimeOffset.Parse(reader.GetString(6)),
        ExpectedTools = reader.IsDBNull(7) ? null : reader.GetString(7),
        OptimalSteps = reader.IsDBNull(8) ? null : reader.GetInt32(8),
    };

    public void Dispose() => _db.Dispose();
}

/// <summary>
/// Aggregate view of past runs. Pass rate answers whether the agent works;
/// tool-use accuracy and trajectory efficiency answer whether it works the right way.
/// </summary>
public sealed class EvalStats
{
    public int TotalRuns { get; set; }
    public int Passed { get; set; }
    public double AverageScore { get; set; }
    public double? AverageToolUseAccuracy { get; set; }
    public double? AverageTrajectoryEfficiency { get; set; }

    public double PassRate => TotalRuns > 0 ? (double)Passed / TotalRuns : 0.0;
}
