using AiAgentCanvas.Abstractions;
using DataConnection.VectorStore.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AiAgentCanvas.Tests;

public class SqliteVectorStoreTests : IDisposable
{
    private readonly TempVectorStore _store = new();
    private SqliteDocumentCollection Docs => _store.Collection;

    public void Dispose() => _store.Dispose();

    private async Task<List<(DocumentRecord Record, double? Score)>> Hybrid(
        float[] query, int top, RagSearchOptions? options = null)
    {
        var results = new List<(DocumentRecord, double?)>();
        await foreach (var hit in Docs.HybridSearchAsync(query, top, options))
            results.Add(hit);
        return results;
    }

    [Fact]
    public async Task A_record_round_trips_with_its_source_tags_version_and_time()
    {
        var at = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        await Docs.UpsertAsync(TempVectorStore.Record("a#1", "Refund policy text.", source: "policy.md", tags: "hr,finance", indexedAt: at, version: 3));

        var back = await Docs.GetAsync("a#1");

        Assert.NotNull(back);
        Assert.Equal("policy.md", back.Source);
        Assert.Equal("hr,finance", back.Tags);
        Assert.Equal(3, back.Version);
        Assert.Equal(at, back.IndexedAt);
        Assert.Equal(HashEmbeddingGenerator.Dimensions, back.Embedding.Length);
    }

    [Fact]
    public async Task A_chunk_that_matches_the_keyword_outranks_a_closer_vector_that_does_not()
    {
        await Docs.UpsertAsync(TempVectorStore.Record("a", "alpha document about cats", [1f, 0f]));
        await Docs.UpsertAsync(TempVectorStore.Record("b", "beta document about dogs", [0.9f, 0.1f]));
        await Docs.UpsertAsync(TempVectorStore.Record("c", "gamma document about zebra crossings", [0f, 1f]));

        var withKeyword = await Hybrid([1f, 0f], 3, new RagSearchOptions { KeywordQuery = "zebra" });
        var vectorOnly = await Hybrid([1f, 0f], 3);

        Assert.Equal("c", withKeyword[0].Record.Id);
        Assert.Equal("a", vectorOnly[0].Record.Id);
    }

    [Fact]
    public async Task Reciprocal_rank_fusion_scores_a_chunk_by_its_ranks_and_not_its_raw_scores()
    {
        await Docs.UpsertAsync(TempVectorStore.Record("a", "first", [1f, 0f]));
        await Docs.UpsertAsync(TempVectorStore.Record("b", "second", [0f, 1f]));

        var results = await Hybrid([1f, 0f], 2);

        Assert.Equal(1.0 / 61, results[0].Score!.Value, 6);
        Assert.Equal(1.0 / 62, results[1].Score!.Value, 6);
    }

    [Fact]
    public async Task A_chunk_found_by_both_rankings_adds_both_contributions()
    {
        await Docs.UpsertAsync(TempVectorStore.Record("a", "zebra", [1f, 0f]));

        var results = await Hybrid([1f, 0f], 1, new RagSearchOptions { KeywordQuery = "zebra" });

        Assert.Equal((1.0 / 61) + (1.0 / 61), results[0].Score!.Value, 6);
    }

    [Fact]
    public async Task The_weighted_mode_is_still_available()
    {
        await Docs.UpsertAsync(TempVectorStore.Record("a", "alpha", [1f, 0f]));

        var results = await Hybrid([1f, 0f], 1, new RagSearchOptions { Fusion = RagFusion.WeightedScore, KeywordQuery = "alpha" });

        // 0.7 * cosine 1.0 + 0.3 * keyword rank 1.
        Assert.Equal(1.0, results[0].Score!.Value, 6);
    }

    [Fact]
    public async Task Source_and_tag_filters_narrow_the_search()
    {
        await Docs.UpsertAsync(TempVectorStore.Record("a", "shared words", source: "one.md", tags: "legal"));
        await Docs.UpsertAsync(TempVectorStore.Record("b", "shared words", source: "two.md", tags: "finance"));

        var bySource = await Hybrid(HashEmbeddingGenerator.Embed("shared words"), 5, new RagSearchOptions { SourceFilter = "two.md" });
        var byTag = await Hybrid(HashEmbeddingGenerator.Embed("shared words"), 5, new RagSearchOptions { TagFilter = "legal" });

        Assert.Equal(["b"], bySource.Select(r => r.Record.Id));
        Assert.Equal(["a"], byTag.Select(r => r.Record.Id));
    }

    [Fact]
    public async Task Deleting_a_chunk_removes_it_from_keyword_search_too()
    {
        await Docs.UpsertAsync(TempVectorStore.Record("a", "unique platypus sentence", [1f, 0f]));
        await Docs.UpsertAsync(TempVectorStore.Record("b", "ordinary text", [0f, 1f]));

        await Docs.DeleteAsync("a");

        Assert.Null(await Docs.GetAsync("a"));
        var results = await Hybrid([0f, 1f], 5, new RagSearchOptions { KeywordQuery = "platypus" });
        Assert.DoesNotContain(results, r => r.Record.Id == "a");
        Assert.Single(results);

        // The keyword index itself holds no row for the chunk.
        Assert.Equal(0, KeywordRows("platypus"));
        Assert.Equal(1, KeywordRows("ordinary"));
    }

    private long KeywordRows(string term)
    {
        using var db = new SqliteConnection(_store.ConnectionString);
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM documents_fts WHERE documents_fts MATCH $term";
        cmd.Parameters.AddWithValue("$term", term);
        return (long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public async Task Replacing_a_chunk_replaces_what_the_keyword_index_knows_about_it()
    {
        await Docs.UpsertAsync(TempVectorStore.Record("a", "old wording about wombats", [1f, 0f]));
        await Docs.UpsertAsync(TempVectorStore.Record("a", "new wording about koalas", [1f, 0f]));

        var oldHit = await Hybrid([0f, 1f], 5, new RagSearchOptions { KeywordQuery = "wombats", Fusion = RagFusion.WeightedScore });
        var newHit = await Hybrid([0f, 1f], 5, new RagSearchOptions { KeywordQuery = "koalas", Fusion = RagFusion.WeightedScore });

        // The word the old text held finds nothing, so the chunk scores on its vector alone.
        Assert.Equal(0.0, oldHit.Single().Score!.Value, 6);
        Assert.True(newHit.Single().Score!.Value > 0.0);
    }

    [Fact]
    public async Task Documents_are_listed_by_source_with_their_chunk_count_and_newest_version()
    {
        var early = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var late = early.AddDays(5);
        await Docs.UpsertAsync(TempVectorStore.Record("a1", "one", source: "a.md", tags: "t", indexedAt: early));
        await Docs.UpsertAsync(TempVectorStore.Record("a2", "two", source: "a.md", tags: "t", indexedAt: early));
        await Docs.UpsertAsync(TempVectorStore.Record("b1", "three", source: "b.md", indexedAt: late, version: 4));
        await Docs.UpsertAsync(TempVectorStore.Record("loose", "no source"));

        var documents = await Docs.ListDocumentsAsync();

        Assert.Equal(["b.md", "a.md"], documents.Select(d => d.Source));
        var a = documents.Single(d => d.Source == "a.md");
        Assert.Equal(2, a.Chunks);
        Assert.Equal(1, a.Version);
        Assert.Equal("t", a.Tags);
        Assert.Equal(4, documents.Single(d => d.Source == "b.md").Version);
    }

    [Fact]
    public async Task Deleting_a_document_removes_every_chunk_that_shares_its_source()
    {
        await Docs.UpsertAsync(TempVectorStore.Record("a1", "one", source: "a.md"));
        await Docs.UpsertAsync(TempVectorStore.Record("a2", "two", source: "a.md"));
        await Docs.UpsertAsync(TempVectorStore.Record("b1", "three", source: "b.md"));

        var removed = await Docs.DeleteDocumentAsync("a.md");

        Assert.Equal(2, removed);
        Assert.Equal(["b.md"], (await Docs.ListDocumentsAsync()).Select(d => d.Source));
        Assert.Equal(0, await Docs.DeleteDocumentAsync("a.md"));
    }

    [Fact]
    public async Task Older_versions_of_a_source_can_be_removed_without_touching_the_newest()
    {
        await Docs.UpsertAsync(TempVectorStore.Record("v1#0", "old", source: "a.md", version: 1));
        await Docs.UpsertAsync(TempVectorStore.Record("v2#0", "new", source: "a.md", version: 2));
        await Docs.UpsertAsync(TempVectorStore.Record("other", "other", source: "b.md", version: 1));

        var removed = await Docs.DeleteOlderVersionsAsync("a.md", 2);

        Assert.Equal(1, removed);
        Assert.Null(await Docs.GetAsync("v1#0"));
        Assert.NotNull(await Docs.GetAsync("v2#0"));
        Assert.NotNull(await Docs.GetAsync("other"));
        Assert.Equal(2, await Docs.LatestVersionAsync("a.md"));
        Assert.Equal(0, await Docs.LatestVersionAsync("missing.md"));
    }

    [Fact]
    public async Task Chunks_indexed_before_a_cutoff_are_removed_and_chunks_with_no_time_are_kept()
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        await Docs.UpsertAsync(TempVectorStore.Record("old", "old", source: "a.md", indexedAt: now.AddDays(-40)));
        await Docs.UpsertAsync(TempVectorStore.Record("new", "new", source: "b.md", indexedAt: now.AddDays(-2)));
        await Docs.UpsertAsync(TempVectorStore.Record("untimed", "untimed", source: "c.md"));

        var removed = await Docs.DeleteIndexedBeforeAsync(now.AddDays(-30));

        Assert.Equal(1, removed);
        Assert.Null(await Docs.GetAsync("old"));
        Assert.NotNull(await Docs.GetAsync("new"));
        Assert.NotNull(await Docs.GetAsync("untimed"));
    }

    [Fact]
    public async Task A_store_written_by_the_earlier_version_is_brought_forward_and_its_deletes_work()
    {
        // The earlier schema: no time or version columns, and a keyword table declared as an
        // external-content table.
        var path = Path.Combine(Path.GetTempPath(), $"legacy-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path}";
        try
        {
            await using (var legacy = new SqliteConnection(connectionString))
            {
                await legacy.OpenAsync();
                await using var create = legacy.CreateCommand();
                create.CommandText = """
                    CREATE TABLE documents (id TEXT PRIMARY KEY, text TEXT NOT NULL, embedding BLOB NOT NULL, metadata_json TEXT, source TEXT, tags TEXT);
                    CREATE VIRTUAL TABLE documents_fts USING fts5(id UNINDEXED, text, content=documents, content_rowid=rowid);
                    INSERT INTO documents (id, text, embedding, source) VALUES ('old1', 'legacy sentence about aardvarks', zeroblob(8), 'legacy.md');
                    """;
                await create.ExecuteNonQueryAsync();
            }

            using var upgraded = new SqliteDocumentCollection(connectionString);
            await upgraded.EnsureCollectionExistsAsync();

            var documents = await upgraded.ListDocumentsAsync();
            Assert.Equal("legacy.md", Assert.Single(documents).Source);
            Assert.Null(documents[0].IndexedAt);

            var hits = new List<(DocumentRecord Record, double? Score)>();
            await foreach (var hit in upgraded.HybridSearchAsync(new float[] { 0f, 0f }, 5, new RagSearchOptions { KeywordQuery = "aardvarks", Fusion = RagFusion.WeightedScore }))
                hits.Add(hit);
            Assert.True(hits.Single().Score!.Value > 0.0, "The rebuilt keyword index should find the legacy chunk.");

            Assert.Equal(1, await upgraded.DeleteDocumentAsync("legacy.md"));
            await upgraded.UpsertAsync(TempVectorStore.Record("fresh", "fresh text", source: "fresh.md"));
            Assert.Equal(1, await upgraded.DeleteDocumentAsync("fresh.md"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
