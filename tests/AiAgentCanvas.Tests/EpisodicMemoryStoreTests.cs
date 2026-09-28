using AiAgentCanvas.Capabilities.EpisodicMemory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiAgentCanvas.Tests;

public class EpisodicMemoryStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly EpisodicMemoryStore _store;

    public EpisodicMemoryStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"episodes-{Guid.NewGuid():N}.db");
        _store = new EpisodicMemoryStore(_dbPath, NullLogger<EpisodicMemoryStore>.Instance)
        {
            ImportanceThreshold = 0.3,
        };
    }

    public void Dispose()
    {
        _store.Dispose();
        try { File.Delete(_dbPath); }
        catch (IOException) { }
    }

    private static Episode Episode(string goal, double importance = 0.8, float[]? embedding = null) => new()
    {
        AgentName = "test",
        Goal = goal,
        Summary = $"summary of {goal}",
        Outcome = "success",
        Importance = importance,
        Embedding = embedding,
    };

    [Fact]
    public void Stores_an_episode_above_the_importance_threshold()
    {
        Assert.True(_store.Save(Episode("ship the release", importance: 0.9)));
        Assert.Single(_store.GetRecent());
    }

    [Fact]
    public void Drops_an_episode_below_the_importance_threshold()
    {
        Assert.False(_store.Save(Episode("checked the time", importance: 0.1)));
        Assert.Empty(_store.GetRecent());
    }

    [Fact]
    public void Keyword_search_still_works_without_embeddings()
    {
        _store.Save(Episode("fix the null reference"));
        _store.Save(Episode("write the deployment guide"));

        var results = _store.Search("null reference");

        Assert.Single(results);
        Assert.Contains("null reference", results[0].Goal);
    }

    [Fact]
    public void Embedding_search_ranks_the_nearest_episode_first()
    {
        _store.Save(Episode("alpha", embedding: [1f, 0f, 0f]));
        _store.Save(Episode("beta", embedding: [0f, 1f, 0f]));
        _store.Save(Episode("gamma", embedding: [0f, 0f, 1f]));

        var results = _store.SearchByEmbedding(new float[] { 0.9f, 0.1f, 0f }, limit: 2);

        Assert.Equal("alpha", results[0].Goal);
    }

    [Fact]
    public void Embedding_survives_the_round_trip_through_sqlite()
    {
        float[] vector = [0.25f, -0.5f, 0.75f];
        _store.Save(Episode("vector round trip", embedding: vector));

        var stored = _store.GetRecent().Single();

        Assert.Equal(vector, stored.Embedding);
    }

    [Fact]
    public void Embedding_search_falls_back_to_keywords_when_nothing_is_embedded()
    {
        _store.Save(Episode("no embedding here"));

        var results = _store.SearchByEmbedding(new float[] { 1f, 0f, 0f }, keywordFallback: "embedding");

        Assert.Single(results);
    }

    [Fact]
    public void Decay_spares_the_episodes_marked_important()
    {
        _store.Save(Episode("routine lookup", importance: 0.3));
        _store.Save(Episode("hard won lesson", importance: 1.0));

        for (var i = 0; i < 10; i++)
            _store.ApplyDecay();

        var byGoal = _store.GetRecent().ToDictionary(e => e.Goal, e => e.RelevanceScore);

        Assert.True(byGoal["hard won lesson"] > byGoal["routine lookup"]);
    }
}
