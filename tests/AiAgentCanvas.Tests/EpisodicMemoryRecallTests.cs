using System.Net;
using System.Text.Json;
using AiAgentCanvas.Capabilities.EpisodicMemory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiAgentCanvas.Tests;

public abstract class EpisodicMemoryTestBase : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"episodes-{Guid.NewGuid():N}.db");

    protected EpisodicMemoryStore Store { get; }

    protected EpisodicMemoryTestBase()
    {
        Store = new EpisodicMemoryStore(_dbPath, NullLogger<EpisodicMemoryStore>.Instance) { ImportanceThreshold = 0.3 };
    }

    protected static Episode Episode(
        string goal, string agent = "test", double importance = 0.8, float[]? embedding = null, string summary = "") => new()
    {
        AgentName = agent,
        Goal = goal,
        Summary = summary == "" ? $"summary of {goal}" : summary,
        Outcome = "success",
        Importance = importance,
        Embedding = embedding,
    };

    public void Dispose()
    {
        Store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }
}

public class EpisodicMemoryMergeTests : EpisodicMemoryTestBase
{
    [Fact]
    public void An_episode_that_repeats_an_earlier_one_updates_it_and_adds_no_second_copy()
    {
        var first = Store.Store(Episode("rotate the api keys", importance: 0.5, embedding: [1f, 0f, 0f], summary: "took four steps"));

        var second = Store.Store(Episode("rotate the api keys", importance: 0.9, embedding: [1f, 0.01f, 0f], summary: "now takes two steps"));

        Assert.True(second.Merged);
        Assert.Equal(first.Id, second.Id);
        var only = Assert.Single(Store.List());
        Assert.Equal("now takes two steps", only.Summary);
        Assert.Equal(0.9, only.Importance, 3);
    }

    [Fact]
    public void A_merge_keeps_the_higher_importance_and_refreshes_relevance()
    {
        var first = Store.Store(Episode("rotate the api keys", importance: 0.9, embedding: [1f, 0f]));
        for (var i = 0; i < 20; i++)
            Store.ApplyDecay();
        Assert.True(Store.Get(first.Id)!.RelevanceScore < 1.0);

        Store.Store(Episode("rotate the api keys", importance: 0.4, embedding: [1f, 0f]));

        var merged = Store.Get(first.Id)!;
        Assert.Equal(0.9, merged.Importance, 3);
        Assert.Equal(1.0, merged.RelevanceScore, 3);
    }

    [Fact]
    public void Episodes_by_different_agents_do_not_merge()
    {
        Store.Store(Episode("rotate the api keys", agent: "ops", embedding: [1f, 0f]));
        Store.Store(Episode("rotate the api keys", agent: "finance", embedding: [1f, 0f]));

        Assert.Equal(2, Store.List().Count);
    }

    [Fact]
    public void Episodes_that_are_not_close_do_not_merge()
    {
        Store.Store(Episode("rotate the api keys", embedding: [1f, 0f]));
        Store.Store(Episode("write the quarterly report", embedding: [0f, 1f]));

        Assert.Equal(2, Store.List().Count);
    }

    [Fact]
    public void Episodes_without_embeddings_cannot_be_compared_so_they_are_kept()
    {
        Store.Store(Episode("rotate the api keys"));
        Store.Store(Episode("rotate the api keys"));

        Assert.Equal(2, Store.List().Count);
    }

    [Fact]
    public void The_closeness_that_counts_as_a_repeat_is_configurable()
    {
        using var strict = new EpisodicMemoryStore(
            Path.Combine(Path.GetTempPath(), $"strict-{Guid.NewGuid():N}.db"), NullLogger<EpisodicMemoryStore>.Instance)
        { DuplicateThreshold = 0.999 };

        strict.Store(Episode("one", embedding: [1f, 0f]));
        strict.Store(Episode("two", embedding: [1f, 0.2f]));

        Assert.Equal(2, strict.List().Count);
    }

    [Fact]
    public void An_episode_below_the_importance_threshold_is_dropped_before_any_comparison()
    {
        Store.Store(Episode("rotate the api keys", embedding: [1f, 0f]));

        var outcome = Store.Store(Episode("rotate the api keys", importance: 0.1, embedding: [1f, 0f]));

        Assert.False(outcome.Stored);
        Assert.False(outcome.Merged);
    }
}

public class EpisodicMemoryReinforcementTests : EpisodicMemoryTestBase
{
    [Fact]
    public void Recalling_an_episode_raises_its_relevance_and_counts_the_recall()
    {
        var saved = Store.Store(Episode("rotate the api keys", importance: 0.2 + 0.2));
        for (var i = 0; i < 10; i++)
            Store.ApplyDecay();
        var decayed = Store.Get(saved.Id)!.RelevanceScore;

        Store.Reinforce([saved.Id]);

        var after = Store.Get(saved.Id)!;
        Assert.Equal(decayed + Store.RecallBoost, after.RelevanceScore, 3);
        Assert.Equal(1, after.RecallCount);
    }

    [Fact]
    public void Relevance_never_rises_above_one()
    {
        var saved = Store.Store(Episode("a lesson"));

        for (var i = 0; i < 5; i++)
            Store.Reinforce([saved.Id]);

        Assert.Equal(1.0, Store.Get(saved.Id)!.RelevanceScore, 3);
        Assert.Equal(5, Store.Get(saved.Id)!.RecallCount);
    }

    [Fact]
    public void An_episode_that_keeps_being_recalled_outlasts_one_that_is_not()
    {
        var used = Store.Store(Episode("used", importance: 0.35));
        var unused = Store.Store(Episode("unused", importance: 0.35));

        for (var i = 0; i < 100; i++)
        {
            Store.ApplyDecay();
            Store.Reinforce([used.Id]);
        }

        Assert.NotNull(Store.Get(used.Id));
        Assert.True(Store.Get(used.Id)!.RelevanceScore > 0.5);
        Assert.True(Store.Get(unused.Id) is null || Store.Get(unused.Id)!.RelevanceScore < 0.1);
    }

    [Fact]
    public void Reinforcing_an_unknown_id_does_nothing()
    {
        Store.Reinforce(["missing"]);

        Assert.Empty(Store.List());
    }
}

public class EpisodicMemoryForgetTests : EpisodicMemoryTestBase
{
    [Fact]
    public void An_episode_can_be_read_listed_and_forgotten()
    {
        var saved = Store.Store(Episode("one"));
        Store.Store(Episode("two"));

        Assert.Equal("one", Store.Get(saved.Id)!.Goal);
        Assert.Equal(2, Store.List().Count);

        Assert.True(Store.Delete(saved.Id));
        Assert.Null(Store.Get(saved.Id));
        Assert.False(Store.Delete(saved.Id));
        Assert.Single(Store.List());
    }

    [Fact]
    public void The_list_includes_episodes_that_decay_has_pushed_out_of_recall()
    {
        var saved = Store.Store(Episode("faded lesson", importance: 0.3));
        for (var i = 0; i < 80; i++)
            Store.ApplyDecay();

        Assert.Empty(Store.Search("faded lesson"));
        Assert.Contains(Store.List(), e => e.Id == saved.Id);
    }

    [Fact]
    public void All_episodes_or_one_agents_episodes_can_be_forgotten()
    {
        Store.Store(Episode("a", agent: "ops"));
        Store.Store(Episode("b", agent: "ops"));
        Store.Store(Episode("c", agent: "finance"));

        Assert.Equal(2, Store.DeleteAll("ops"));
        Assert.Equal(["finance"], Store.List().Select(e => e.AgentName));
        Assert.Equal(1, Store.DeleteAll());
        Assert.Empty(Store.List());
    }

    [Fact]
    public void The_list_pages()
    {
        for (var i = 0; i < 5; i++)
            Store.Store(Episode($"episode {i}"));

        Assert.Equal(2, Store.List(limit: 2).Count);
        Assert.Equal(3, Store.List(limit: 10, offset: 2).Count);
    }
}

public class EpisodicMemoryToolTests : EpisodicMemoryTestBase
{
    private IReadOnlyList<AITool> Tools(IEmbeddingGenerator<string, Embedding<float>>? embedder = null) =>
        EpisodicMemoryToolProvider.CreateTools(Store, embedder);

    private static async Task<JsonElement> CallAsync(IReadOnlyList<AITool> tools, string name, Dictionary<string, object?> args)
    {
        var tool = tools.OfType<AIFunction>().Single(t => t.Name == name);
        var result = await tool.InvokeAsync(new AIFunctionArguments(args));
        return JsonDocument.Parse(result is JsonElement e ? e.GetString()! : result!.ToString()!).RootElement;
    }

    private static Dictionary<string, object?> Save(string goal, double importance = 0.8) => new()
    {
        ["goal"] = goal, ["summary"] = "what happened", ["outcome"] = "success",
        ["toolsUsed"] = Array.Empty<string>(), ["turnCount"] = 3, ["importance"] = importance,
    };

    [Fact]
    public void The_tools_are_search_recent_save_and_forget()
    {
        Assert.Equal(["forget_memory", "recall_recent_memory", "save_to_memory", "search_memory"],
            Tools().Select(t => t.Name).OrderBy(n => n));
    }

    [Fact]
    public async Task Saving_a_repeat_says_it_was_merged()
    {
        var embedder = new HashEmbeddingGenerator();
        var tools = Tools(embedder);

        var first = await CallAsync(tools, "save_to_memory", Save("rotate the api keys"));
        var second = await CallAsync(tools, "save_to_memory", Save("rotate the api keys"));

        Assert.False(first.GetProperty("merged").GetBoolean());
        Assert.True(second.GetProperty("merged").GetBoolean());
        Assert.Equal(first.GetProperty("Id").GetString(), second.GetProperty("Id").GetString());
        Assert.Contains("no second copy", second.GetProperty("note").GetString());
        Assert.Single(Store.List());
    }

    [Fact]
    public async Task Searching_memory_refreshes_the_episodes_it_returns()
    {
        var tools = Tools(new HashEmbeddingGenerator());
        var saved = await CallAsync(tools, "save_to_memory", Save("rotate the api keys"));
        var id = saved.GetProperty("Id").GetString()!;

        var found = await CallAsync(tools, "search_memory", new() { ["query"] = "rotate the api keys", ["agentFilter"] = null, ["limit"] = null });

        Assert.Equal(id, found[0].GetProperty("Id").GetString());
        Assert.Equal(1, Store.Get(id)!.RecallCount);
    }

    [Fact]
    public async Task Forgetting_by_id_removes_the_episode_and_a_wrong_id_says_so()
    {
        var tools = Tools(new HashEmbeddingGenerator());
        var saved = await CallAsync(tools, "save_to_memory", Save("something to forget"));
        var id = saved.GetProperty("Id").GetString()!;

        var gone = await CallAsync(tools, "forget_memory", new() { ["episodeId"] = id });
        var again = await CallAsync(tools, "forget_memory", new() { ["episodeId"] = id });

        Assert.True(gone.GetProperty("forgotten").GetBoolean());
        Assert.False(again.GetProperty("forgotten").GetBoolean());
        Assert.Empty(Store.List());
    }
}

public class EpisodicMemoryEndpointTests : EpisodicMemoryTestBase
{
    private async Task<(WebApplication App, HttpClient Client)> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddLogging();
        builder.Services.AddSingleton(Store);
        var app = builder.Build();
        app.MapEpisodicMemoryEndpoints();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (app, new HttpClient { BaseAddress = new Uri(address) });
    }

    [Fact]
    public async Task A_person_can_list_read_and_delete_episodes()
    {
        var saved = Store.Store(Episode("rotate the api keys", embedding: [1f, 0f]));
        var (app, client) = await StartAsync();
        await using var server = app;

        var list = JsonDocument.Parse(await client.GetStringAsync("/api/memory/episodes")).RootElement;
        Assert.Equal(saved.Id, list[0].GetProperty("id").GetString());
        Assert.True(list[0].GetProperty("embedded").GetBoolean());
        Assert.False(list[0].TryGetProperty("embedding", out _), "The vector itself is not sent.");

        var one = await client.GetAsync($"/api/memory/episodes/{saved.Id}");
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/memory/episodes/{saved.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/memory/episodes/{saved.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/memory/episodes/{saved.Id}")).StatusCode);
    }

    [Fact]
    public async Task Deleting_everything_needs_an_explicit_confirmation()
    {
        Store.Store(Episode("one", agent: "ops"));
        Store.Store(Episode("two", agent: "finance"));
        var (app, client) = await StartAsync();
        await using var server = app;

        var refused = await client.DeleteAsync("/api/memory/episodes");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(2, Store.List().Count);

        var one = await client.DeleteAsync("/api/memory/episodes?agent=ops&confirm=true");
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        Assert.Equal(["finance"], Store.List().Select(e => e.AgentName));

        await client.DeleteAsync("/api/memory/episodes?confirm=true");
        Assert.Empty(Store.List());
    }
}
