using System.Globalization;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AiAgentCanvas.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.VectorData;

namespace DataConnection.VectorStore.Sqlite;

public sealed class SqliteDocumentCollection : VectorStoreCollection<string, DocumentRecord>, IHybridSearchable, IDocumentIndex
{
    private const string Columns = "id, text, embedding, metadata_json, source, tags, indexed_at, version";

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

        await MigrateColumnsAsync(ct);
        await EnsureKeywordIndexAsync(ct);
    }

    private async Task MigrateColumnsAsync(CancellationToken ct)
    {
        var columns = new HashSet<string>();
        await using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA table_info([{_collectionName}])";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                columns.Add(reader.GetString(1));
        }

        string[] additions =
        [
            "source TEXT",
            "tags TEXT",
            "indexed_at TEXT",
            "version INTEGER NOT NULL DEFAULT 1",
        ];

        foreach (var definition in additions)
        {
            if (columns.Contains(definition.Split(' ')[0]))
                continue;

            await using var alter = _connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE [{_collectionName}] ADD COLUMN {definition}";
            await alter.ExecuteNonQueryAsync(ct);
        }

        await using var index = _connection.CreateCommand();
        index.CommandText = $"CREATE INDEX IF NOT EXISTS [idx_{_collectionName}_source] ON [{_collectionName}](source)";
        await index.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// The keyword index is a table of its own. Earlier versions declared it as an
    /// external-content table over the documents but wrote to it directly, which leaves
    /// deletes unable to find their rows. A table in that older form is dropped and rebuilt
    /// from the documents.
    /// </summary>
    private async Task EnsureKeywordIndexAsync(CancellationToken ct)
    {
        var fts = $"{_collectionName}_fts";

        string? definition;
        await using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=@name";
            cmd.Parameters.AddWithValue("@name", fts);
            definition = (await cmd.ExecuteScalarAsync(ct)) as string;
        }

        var rebuild = definition is not null && definition.Contains("content=", StringComparison.OrdinalIgnoreCase);
        if (rebuild)
        {
            await using var drop = _connection.CreateCommand();
            drop.CommandText = $"DROP TABLE [{fts}]";
            await drop.ExecuteNonQueryAsync(ct);
        }

        if (definition is null || rebuild)
        {
            await using var create = _connection.CreateCommand();
            create.CommandText = $"CREATE VIRTUAL TABLE [{fts}] USING fts5(id UNINDEXED, text)";
            await create.ExecuteNonQueryAsync(ct);

            await using var fill = _connection.CreateCommand();
            fill.CommandText = $"INSERT INTO [{fts}] (id, text) SELECT id, text FROM [{_collectionName}]";
            await fill.ExecuteNonQueryAsync(ct);
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
        cmd.CommandText = $"SELECT {Columns} FROM [{_collectionName}] WHERE id = @id";
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
            INSERT OR REPLACE INTO [{_collectionName}] (id, text, embedding, metadata_json, source, tags, indexed_at, version)
            VALUES (@id, @text, @embedding, @metadata, @source, @tags, @indexed, @version)
            """;
        cmd.Parameters.AddWithValue("@id", record.Id);
        cmd.Parameters.AddWithValue("@text", record.Text);
        cmd.Parameters.AddWithValue("@embedding", SerializeEmbedding(record.Embedding));
        cmd.Parameters.AddWithValue("@metadata", (object?)record.MetadataJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@source", (object?)record.Source ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tags", (object?)record.Tags ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@indexed", record.IndexedAt is { } at ? Format(at) : DBNull.Value);
        cmd.Parameters.AddWithValue("@version", record.Version);
        await cmd.ExecuteNonQueryAsync(ct);

        // The keyword index has no unique key, so a replacement removes the old row first.
        await using var removeFts = _connection.CreateCommand();
        removeFts.CommandText = $"DELETE FROM [{_collectionName}_fts] WHERE id = @id";
        removeFts.Parameters.AddWithValue("@id", record.Id);
        await removeFts.ExecuteNonQueryAsync(ct);

        await using var ftsCmd = _connection.CreateCommand();
        ftsCmd.CommandText = $"INSERT INTO [{_collectionName}_fts] (id, text) VALUES (@id, @text)";
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

    public override async Task DeleteAsync(string key, CancellationToken ct = default) =>
        await DeleteWhereAsync("id = @p", key, ct);

    public override async Task DeleteAsync(IEnumerable<string> keys, CancellationToken ct = default)
    {
        foreach (var key in keys)
            await DeleteAsync(key, ct);
    }

    // ---- document-level operations ----

    public async Task<IReadOnlyList<IndexedDocument>> ListDocumentsAsync(CancellationToken ct = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT source, COUNT(*), MAX(version), MAX(indexed_at), MAX(tags)
            FROM [{_collectionName}]
            WHERE source IS NOT NULL
            GROUP BY source
            ORDER BY MAX(indexed_at) DESC, source
            """;

        var documents = new List<IndexedDocument>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            documents.Add(new IndexedDocument(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : Parse(reader.GetString(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }
        return documents;
    }

    public async Task<int> LatestVersionAsync(string source, CancellationToken ct = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT COALESCE(MAX(version), 0) FROM [{_collectionName}] WHERE source = @source";
        cmd.Parameters.AddWithValue("@source", source);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public Task<int> DeleteDocumentAsync(string source, CancellationToken ct = default) =>
        DeleteWhereAsync("source = @p", source, ct);

    public async Task<int> DeleteOlderVersionsAsync(string source, int version, CancellationToken ct = default)
    {
        const string where = "source = @p AND version < @version";
        return await DeleteWhereAsync(where, source, ct, cmd => cmd.Parameters.AddWithValue("@version", version));
    }

    public Task<int> DeleteIndexedBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default) =>
        DeleteWhereAsync("indexed_at IS NOT NULL AND indexed_at < @p", Format(cutoff), ct);

    /// <summary>Removes the matching rows from the keyword index first, while the main table can still name them.</summary>
    private async Task<int> DeleteWhereAsync(string where, string value, CancellationToken ct, Action<SqliteCommand>? bind = null)
    {
        await using (var fts = _connection.CreateCommand())
        {
            fts.CommandText = $"DELETE FROM [{_collectionName}_fts] WHERE id IN (SELECT id FROM [{_collectionName}] WHERE {where})";
            fts.Parameters.AddWithValue("@p", value);
            bind?.Invoke(fts);
            await fts.ExecuteNonQueryAsync(ct);
        }

        await using var main = _connection.CreateCommand();
        main.CommandText = $"DELETE FROM [{_collectionName}] WHERE {where}";
        main.Parameters.AddWithValue("@p", value);
        bind?.Invoke(main);
        return await main.ExecuteNonQueryAsync(ct);
    }

    // ---- search ----

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
        cmd.CommandText = $"SELECT {Columns} FROM [{_collectionName}]";

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

        // Rank the documents that pass the filters by similarity to the query.
        var scored = new List<(DocumentRecord Record, double Cosine)>();
        await using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = $"SELECT {Columns} FROM [{_collectionName}]{BuildFilterClause(options)}";
            AddFilterParameters(cmd, options);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var record = ReadRecord(reader);
                scored.Add((record, CosineSimilarity(queryEmbedding.Span, record.Embedding.Span)));
            }
        }

        var byVector = scored.OrderByDescending(s => s.Cosine).ToList();
        var keywordRank = await KeywordRanksAsync(options.KeywordQuery, Math.Max(top * 3, 30), ct);

        IEnumerable<(DocumentRecord Record, double Score)> ranked = options.Fusion == RagFusion.WeightedScore
            ? WeightedFusion(byVector, keywordRank, options)
            : ReciprocalRankFusion(byVector, keywordRank, options, Math.Max(top * 3, 30));

        foreach (var (record, score) in ranked.OrderByDescending(r => r.Score).Take(top))
            yield return (record, score);
    }

    /// <summary>The ids that match the keyword query, best first, mapped to their 1-based rank.</summary>
    private async Task<Dictionary<string, int>> KeywordRanksAsync(string? query, int limit, CancellationToken ct)
    {
        var ranks = new Dictionary<string, int>();
        if (string.IsNullOrWhiteSpace(query))
            return ranks;

        var ftsQuery = SanitizeFtsQuery(query);
        if (string.IsNullOrWhiteSpace(ftsQuery))
            return ranks;

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT id FROM [{_collectionName}_fts]
            WHERE [{_collectionName}_fts] MATCH @query
            ORDER BY rank
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@query", ftsQuery);
        cmd.Parameters.AddWithValue("@limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            ranks[reader.GetString(0)] = ranks.Count + 1;
        return ranks;
    }

    private static IEnumerable<(DocumentRecord Record, double Score)> ReciprocalRankFusion(
        List<(DocumentRecord Record, double Cosine)> byVector,
        Dictionary<string, int> keywordRank,
        RagSearchOptions options,
        int pool)
    {
        var k = Math.Max(1, options.RankConstant);

        for (var i = 0; i < byVector.Count; i++)
        {
            var (record, _) = byVector[i];
            var score = i < pool ? 1.0 / (k + i + 1) : 0.0;

            if (keywordRank.TryGetValue(record.Id, out var rank))
                score += 1.0 / (k + rank);

            // A chunk outside both pools has nothing to contribute. It still ranks, last.
            yield return (record, score);
        }
    }

    private static IEnumerable<(DocumentRecord Record, double Score)> WeightedFusion(
        List<(DocumentRecord Record, double Cosine)> byVector,
        Dictionary<string, int> keywordRank,
        RagSearchOptions options)
    {
        foreach (var (record, cosine) in byVector)
        {
            var keyword = keywordRank.TryGetValue(record.Id, out var rank) ? 1.0 / rank : 0.0;
            yield return (record, (options.VectorWeight * cosine) + (options.KeywordWeight * keyword));
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
            IndexedAt = reader.IsDBNull(6) ? null : Parse(reader.GetString(6)),
            Version = reader.IsDBNull(7) ? 1 : reader.GetInt32(7),
        };
    }

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

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
