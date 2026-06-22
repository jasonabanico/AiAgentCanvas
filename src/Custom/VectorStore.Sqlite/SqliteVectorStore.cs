using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AiAgentCanvas.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.VectorData;

namespace VectorStore.Sqlite;

public sealed class SqliteDocumentCollection : VectorStoreCollection<string, DocumentRecord>, IHybridSearchable
{
    private readonly SqliteConnection _connection;
    private readonly string _collectionName;

    public SqliteDocumentCollection(string connectionString, string collectionName = "documents")
    {
        _collectionName = collectionName;
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
    }

    public override string Name => _collectionName;

    public override async Task<bool> CollectionExistsAsync(CancellationToken ct = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
        cmd.Parameters.AddWithValue("@name", _collectionName);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result) > 0;
    }

    public override async Task EnsureCollectionExistsAsync(CancellationToken ct = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS [{_collectionName}] (
                id TEXT PRIMARY KEY,
                text TEXT NOT NULL,
                embedding BLOB NOT NULL,
                metadata_json TEXT,
                source TEXT,
                tags TEXT
            )
            """;
        await cmd.ExecuteNonQueryAsync(ct);

        await using var ftsCmd = _connection.CreateCommand();
        ftsCmd.CommandText = $"""
            CREATE VIRTUAL TABLE IF NOT EXISTS [{_collectionName}_fts]
            USING fts5(id UNINDEXED, text, content=[{_collectionName}], content_rowid=rowid)
            """;
        await ftsCmd.ExecuteNonQueryAsync(ct);

        await MigrateColumnsAsync(ct);
    }

    private async Task MigrateColumnsAsync(CancellationToken ct)
    {
        var columns = new HashSet<string>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info([{_collectionName}])";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            columns.Add(reader.GetString(1));

        if (!columns.Contains("source"))
        {
            await using var alter = _connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE [{_collectionName}] ADD COLUMN source TEXT";
            await alter.ExecuteNonQueryAsync(ct);
        }

        if (!columns.Contains("tags"))
        {
            await using var alter = _connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE [{_collectionName}] ADD COLUMN tags TEXT";
            await alter.ExecuteNonQueryAsync(ct);
        }
    }

    public override async Task EnsureCollectionDeletedAsync(CancellationToken ct = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"DROP TABLE IF EXISTS [{_collectionName}_fts]";
        await cmd.ExecuteNonQueryAsync(ct);

        await using var cmd2 = _connection.CreateCommand();
        cmd2.CommandText = $"DROP TABLE IF EXISTS [{_collectionName}]";
        await cmd2.ExecuteNonQueryAsync(ct);
    }

    public override async Task<DocumentRecord?> GetAsync(string key, RecordRetrievalOptions? options = null, CancellationToken ct = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT id, text, embedding, metadata_json, source, tags FROM [{_collectionName}] WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", key);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return ReadRecord(reader);
    }

    public override async IAsyncEnumerable<DocumentRecord> GetAsync(
        IEnumerable<string> keys,
        RecordRetrievalOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var key in keys)
        {
            var record = await GetAsync(key, options, ct);
            if (record is not null)
                yield return record;
        }
    }

    public override IAsyncEnumerable<DocumentRecord> GetAsync(
        Expression<Func<DocumentRecord, bool>> filter,
        int top,
        FilteredRecordRetrievalOptions<DocumentRecord>? options = null,
        CancellationToken ct = default)
    {
        throw new NotSupportedException("Expression-based filtering is not supported by the SQLite vector store.");
    }

    public override async Task<string> UpsertAsync(DocumentRecord record, CancellationToken ct = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT OR REPLACE INTO [{_collectionName}] (id, text, embedding, metadata_json, source, tags)
            VALUES (@id, @text, @embedding, @metadata, @source, @tags)
            """;
        cmd.Parameters.AddWithValue("@id", record.Id);
        cmd.Parameters.AddWithValue("@text", record.Text);
        cmd.Parameters.AddWithValue("@embedding", SerializeEmbedding(record.Embedding));
        cmd.Parameters.AddWithValue("@metadata", (object?)record.MetadataJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@source", (object?)record.Source ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tags", (object?)record.Tags ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);

        await using var ftsCmd = _connection.CreateCommand();
        ftsCmd.CommandText = $"""
            INSERT OR REPLACE INTO [{_collectionName}_fts] (id, text)
            VALUES (@id, @text)
            """;
        ftsCmd.Parameters.AddWithValue("@id", record.Id);
        ftsCmd.Parameters.AddWithValue("@text", record.Text);
        await ftsCmd.ExecuteNonQueryAsync(ct);

        return record.Id;
    }

    public override async Task UpsertAsync(
        IEnumerable<DocumentRecord> records,
        CancellationToken ct = default)
    {
        foreach (var record in records)
            await UpsertAsync(record, ct);
    }

    public override async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        await using var ftsCmd = _connection.CreateCommand();
        ftsCmd.CommandText = $"DELETE FROM [{_collectionName}_fts] WHERE id = @id";
        ftsCmd.Parameters.AddWithValue("@id", key);
        await ftsCmd.ExecuteNonQueryAsync(ct);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"DELETE FROM [{_collectionName}] WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", key);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public override async Task DeleteAsync(IEnumerable<string> keys, CancellationToken ct = default)
    {
        foreach (var key in keys)
            await DeleteAsync(key, ct);
    }

    public override async IAsyncEnumerable<VectorSearchResult<DocumentRecord>> SearchAsync<TInput>(
        TInput value,
        int top,
        VectorSearchOptions<DocumentRecord>? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ReadOnlyMemory<float> queryEmbedding = value switch
        {
            ReadOnlyMemory<float> mem => mem,
            float[] arr => arr,
            _ => throw new NotSupportedException(
                $"Search input type '{typeof(TInput).Name}' is not supported. Use ReadOnlyMemory<float> or float[].")
        };

        var results = new List<(DocumentRecord Record, double Score)>();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT id, text, embedding, metadata_json, source, tags FROM [{_collectionName}]";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var record = ReadRecord(reader);
            var score = CosineSimilarity(queryEmbedding.Span, record.Embedding.Span);
            results.Add((record, score));
        }

        foreach (var (record, score) in results.OrderByDescending(r => r.Score).Take(top))
        {
            yield return new VectorSearchResult<DocumentRecord>(record, score);
        }
    }

    public async IAsyncEnumerable<(DocumentRecord Record, double? Score)> HybridSearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int top,
        RagSearchOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        options ??= new RagSearchOptions();
        var keywordScores = new Dictionary<string, double>();

        if (!string.IsNullOrWhiteSpace(options.KeywordQuery))
        {
            var ftsQuery = SanitizeFtsQuery(options.KeywordQuery);
            if (!string.IsNullOrWhiteSpace(ftsQuery))
            {
                await using var ftsCmd = _connection.CreateCommand();
                ftsCmd.CommandText = $"""
                    SELECT id, rank FROM [{_collectionName}_fts]
                    WHERE [{_collectionName}_fts] MATCH @query
                    ORDER BY rank
                    LIMIT @limit
                    """;
                ftsCmd.Parameters.AddWithValue("@query", ftsQuery);
                ftsCmd.Parameters.AddWithValue("@limit", top * 3);

                await using var ftsReader = await ftsCmd.ExecuteReaderAsync(ct);
                while (await ftsReader.ReadAsync(ct))
                {
                    var id = ftsReader.GetString(0);
                    var rank = ftsReader.GetDouble(1);
                    keywordScores[id] = 1.0 / (1.0 + Math.Abs(rank));
                }
            }
        }

        var results = new List<(DocumentRecord Record, double Score)>();
        var whereClause = BuildFilterClause(options);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT id, text, embedding, metadata_json, source, tags FROM [{_collectionName}]{whereClause}";
        AddFilterParameters(cmd, options);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var record = ReadRecord(reader);
            var vectorScore = CosineSimilarity(queryEmbedding.Span, record.Embedding.Span);

            double finalScore;
            if (keywordScores.TryGetValue(record.Id, out var kwScore))
                finalScore = (options.VectorWeight * vectorScore) + (options.KeywordWeight * kwScore);
            else
                finalScore = options.VectorWeight * vectorScore;

            results.Add((record, finalScore));
        }

        foreach (var (record, score) in results.OrderByDescending(r => r.Score).Take(top))
        {
            yield return (record, score);
        }
    }

    private static string BuildFilterClause(RagSearchOptions options)
    {
        var conditions = new List<string>();

        if (!string.IsNullOrWhiteSpace(options.SourceFilter))
            conditions.Add("source = @source");

        if (!string.IsNullOrWhiteSpace(options.TagFilter))
            conditions.Add("tags LIKE @tag");

        return conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
    }

    private static void AddFilterParameters(SqliteCommand cmd, RagSearchOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.SourceFilter))
            cmd.Parameters.AddWithValue("@source", options.SourceFilter);

        if (!string.IsNullOrWhiteSpace(options.TagFilter))
            cmd.Parameters.AddWithValue("@tag", $"%{options.TagFilter}%");
    }

    private static string SanitizeFtsQuery(string query)
    {
        var words = Regex.Split(query, @"\W+")
            .Where(w => w.Length > 1)
            .Select(w => $"\"{w}\"");
        return string.Join(" OR ", words);
    }

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(VectorStoreCollection<string, DocumentRecord>))
            return this;
        return null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _connection.Dispose();
        base.Dispose(disposing);
    }

    private static DocumentRecord ReadRecord(SqliteDataReader reader)
    {
        return new DocumentRecord
        {
            Id = reader.GetString(0),
            Text = reader.GetString(1),
            Embedding = DeserializeEmbedding((byte[])reader.GetValue(2)),
            MetadataJson = reader.IsDBNull(3) ? null : reader.GetString(3),
            Source = reader.IsDBNull(4) ? null : reader.GetString(4),
            Tags = reader.IsDBNull(5) ? null : reader.GetString(5),
        };
    }

    private static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length) return 0;

        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        var magnitude = Math.Sqrt(magA) * Math.Sqrt(magB);
        return magnitude == 0 ? 0 : dot / magnitude;
    }

    private static byte[] SerializeEmbedding(ReadOnlyMemory<float> embedding)
    {
        var span = embedding.Span;
        var bytes = new byte[span.Length * sizeof(float)];
        for (var i = 0; i < span.Length; i++)
            BitConverter.TryWriteBytes(bytes.AsSpan(i * sizeof(float)), span[i]);
        return bytes;
    }

    private static ReadOnlyMemory<float> DeserializeEmbedding(byte[] bytes)
    {
        var floats = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }
}
