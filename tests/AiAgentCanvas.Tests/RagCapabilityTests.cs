#pragma warning disable MAAI001

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Capabilities.Rag;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.VectorData;
using Xunit;

namespace AiAgentCanvas.Tests;

public abstract class RagTestBase : IDisposable
{
    protected readonly TempVectorStore Store = new();
    protected readonly HashEmbeddingGenerator Embedder = new();
    protected readonly FixedTimeProvider Clock = new(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));

    protected RagOptions Options { get; } = new() { Rerank = false };

    protected RagIngestionService Ingestion(DocumentChunker? chunker = null) => new(
        Store.Collection, Store.Collection, Embedder,
        chunker ?? new DocumentChunker { ChunkSize = 120, ChunkOverlap = 20 },
        Options, NullLogger<RagIngestionService>.Instance, Clock);

    protected RagSearcher Searcher() => new(Store.Collection, Embedder, Options);

    protected IReadOnlyList<AITool> Tools() => RagToolProvider.CreateTools(Searcher(), Store.Collection, Options);

    protected static async Task<JsonElement> CallAsync(IReadOnlyList<AITool> tools, string name, Dictionary<string, object?> args)
    {
        var tool = tools.OfType<AIFunction>().Single(t => t.Name == name);
        var result = await tool.InvokeAsync(new AIFunctionArguments(args));
        return JsonDocument.Parse(result is JsonElement e ? e.GetString()! : result!.ToString()!).RootElement;
    }

    protected const string Handbook = """
        Employees accrue vacation at two days per month of service.

        Unused vacation carries over for twelve months, then expires.

        Expense reports are due on the fifth business day of the following month.
        """;

    public void Dispose() => Store.Dispose();
}

public class RagIngestionTests : RagTestBase
{
    [Fact]
    public async Task A_document_is_split_embedded_and_stored_with_its_source_tags_version_and_time()
    {
        var result = await Ingestion().IngestAsync("handbook.md", Handbook, "hr");

        Assert.Equal(1, result.Version);
        Assert.True(result.Chunks >= 2);
        Assert.Equal(0, result.ReplacedChunks);

        var document = Assert.Single(await Store.Collection.ListDocumentsAsync());
        Assert.Equal("handbook.md", document.Source);
        Assert.Equal(result.Chunks, document.Chunks);
        Assert.Equal("hr", document.Tags);
        Assert.Equal(Clock.Now, document.IndexedAt);
    }

    [Fact]
    public async Task Chunks_are_embedded_in_batches_of_the_configured_size()
    {
        Options.EmbeddingBatchSize = 2;
        var longer = string.Join(Environment.NewLine + Environment.NewLine,
            Enumerable.Range(1, 8).Select(i => $"Section {i} describes policy number {i} in enough words to fill a chunk on its own, with detail."));

        var result = await Ingestion().IngestAsync("handbook.md", longer);

        Assert.True(result.Chunks >= 3);
        Assert.All(Embedder.BatchSizes, size => Assert.True(size <= 2));
        Assert.Equal(result.Chunks, Embedder.BatchSizes.Sum());
    }

    [Fact]
    public async Task Indexing_a_source_again_replaces_its_earlier_version()
    {
        var ingestion = Ingestion();
        var first = await ingestion.IngestAsync("handbook.md", Handbook);

        var second = await ingestion.IngestAsync("handbook.md", "Vacation now accrues at three days per month of service, with no expiry for unused days.");

        Assert.Equal(2, second.Version);
        Assert.Equal(first.Chunks, second.ReplacedChunks);

        var document = Assert.Single(await Store.Collection.ListDocumentsAsync());
        Assert.Equal(2, document.Version);
        Assert.Equal(second.Chunks, document.Chunks);

        var hits = await Searcher().SearchAsync(["expense reports"], topK: 5);
        Assert.DoesNotContain(hits, h => h.Record.Text.Contains("Expense", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_failed_embedding_call_leaves_the_earlier_version_in_place()
    {
        var ingestion = Ingestion();
        var first = await ingestion.IngestAsync("handbook.md", Handbook);
        Embedder.FailOnCall = Embedder.BatchSizes.Count + 1;

        await Assert.ThrowsAsync<InvalidOperationException>(() => ingestion.IngestAsync("handbook.md", "A replacement that never gets stored."));

        var document = Assert.Single(await Store.Collection.ListDocumentsAsync());
        Assert.Equal(1, document.Version);
        Assert.Equal(first.Chunks, document.Chunks);
    }

    [Fact]
    public async Task Two_sources_keep_separate_versions()
    {
        var ingestion = Ingestion();
        await ingestion.IngestAsync("a.md", Handbook);
        await ingestion.IngestAsync("a.md", Handbook);

        var b = await ingestion.IngestAsync("b.md", Handbook);

        Assert.Equal(1, b.Version);
        Assert.Equal(2, (await Store.Collection.ListDocumentsAsync()).Count);
    }

    [Theory]
    [InlineData("", "text that is long enough to be a chunk", "Name the source")]
    [InlineData("  ", "text that is long enough to be a chunk", "Name the source")]
    [InlineData("a.md", "", "no text")]
    [InlineData("a.md", "short", "no usable text")]
    public async Task A_request_that_cannot_be_indexed_says_why(string source, string text, string expected)
    {
        var ex = await Assert.ThrowsAsync<RagIngestionException>(() => Ingestion().IngestAsync(source, text));

        Assert.Contains(expected, ex.Message);
        Assert.Empty(await Store.Collection.ListDocumentsAsync());
    }

    [Fact]
    public async Task A_document_over_the_size_limit_is_refused()
    {
        Options.MaxDocumentChars = 100;

        var ex = await Assert.ThrowsAsync<RagIngestionException>(() => Ingestion().IngestAsync("big.md", new string('x', 101)));

        Assert.Contains("limit", ex.Message);
    }

    [Fact]
    public async Task A_document_is_deleted_by_source()
    {
        var ingestion = Ingestion();
        var result = await ingestion.IngestAsync("handbook.md", Handbook);

        Assert.Equal(result.Chunks, await ingestion.DeleteAsync("handbook.md"));
        Assert.Empty(await Store.Collection.ListDocumentsAsync());
        Assert.Equal(0, await ingestion.DeleteAsync("handbook.md"));
    }
}

public class RagSearchTests : RagTestBase
{
    private async Task Seed()
    {
        var ingestion = Ingestion();
        await ingestion.IngestAsync("handbook.md", Handbook, "hr");
        await ingestion.IngestAsync("security.md", "Passwords must be rotated every ninety days and never shared. Report phishing to the security desk.", "it");
    }

    [Fact]
    public async Task A_question_finds_the_passage_that_answers_it()
    {
        await Seed();

        var hits = await Searcher().SearchAsync(["when are expense reports due"], topK: 1);

        Assert.Equal("handbook.md", hits.Single().Record.Source);
        Assert.Contains("Expense reports", hits.Single().Record.Text);
    }

    [Fact]
    public async Task The_source_and_tag_filters_apply()
    {
        await Seed();

        var bySource = await Searcher().SearchAsync(["rotated passwords vacation"], topK: 5, source: "security.md");
        var byTag = await Searcher().SearchAsync(["rotated passwords vacation"], topK: 5, tag: "hr");

        Assert.All(bySource, h => Assert.Equal("security.md", h.Record.Source));
        Assert.All(byTag, h => Assert.Equal("handbook.md", h.Record.Source));
    }

    [Fact]
    public void A_passage_that_ranks_well_under_several_phrasings_rises_above_one_that_matches_a_single_phrasing()
    {
        List<List<VectorSearchResult<DocumentRecord>>> lists =
        [
            [Hit("x"), Hit("shared")],
            [Hit("y"), Hit("shared")],
            [Hit("z"), Hit("shared")],
        ];

        var fused = RagSearcher.Fuse(lists, take: 4);

        Assert.Equal("shared", fused[0].Record.Id);
        Assert.Equal(4, fused.Count);
        Assert.Equal(3 * (1.0 / 62), fused[0].Score!.Value, 6);
    }

    private static VectorSearchResult<DocumentRecord> Hit(string id) =>
        new(new DocumentRecord { Id = id, Text = id }, 0);

    [Fact]
    public async Task Several_phrasings_are_searched_and_blank_ones_are_ignored()
    {
        await Seed();
        var before = Embedder.BatchSizes.Count;

        var hits = await Searcher().SearchAsync(["", "vacation carry over", "  ", "phishing report"], topK: 4);

        Assert.Contains(hits, h => h.Record.Source == "handbook.md");
        Assert.Contains(hits, h => h.Record.Source == "security.md");
        Assert.Equal(2, Embedder.BatchSizes.Count - before);
    }

    [Fact]
    public async Task No_phrasings_means_no_results_and_no_embedding_call()
    {
        var before = Embedder.BatchSizes.Count;

        Assert.Empty(await Searcher().SearchAsync(["", "  "]));
        Assert.Equal(before, Embedder.BatchSizes.Count);
    }

    [Fact]
    public async Task The_search_tool_returns_cited_passages()
    {
        await Seed();

        var result = await CallAsync(Tools(), "rag_search", new() { ["queries"] = new[] { "vacation accrual" }, ["topK"] = 2 });

        Assert.True(result.GetProperty("count").GetInt32() > 0);
        var first = result.GetProperty("results")[0];
        Assert.Equal("handbook.md", first.GetProperty("source").GetString());
        Assert.Equal(1, first.GetProperty("version").GetInt32());
        Assert.Contains("vacation", first.GetProperty("text").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_search_tool_asks_for_a_query_and_explains_an_empty_result()
    {
        var none = await CallAsync(Tools(), "rag_search", new() { ["queries"] = new[] { " " } });
        Assert.Contains("at least one query", none.GetProperty("error").GetString());

        var empty = await CallAsync(Tools(), "rag_search", new() { ["queries"] = new[] { "anything" } });
        Assert.Equal(0, empty.GetProperty("count").GetInt32());
        Assert.Contains("No passage matched", empty.GetProperty("note").GetString());
    }

    [Fact]
    public async Task The_search_tool_uses_no_more_phrasings_than_the_option_allows()
    {
        await Seed();
        Options.MaxQueriesPerSearch = 2;
        var before = Embedder.BatchSizes.Count;

        await CallAsync(Tools(), "rag_search", new() { ["queries"] = new[] { "one", "two", "three", "four" } });

        Assert.Equal(2, Embedder.BatchSizes.Count - before);
    }

    [Fact]
    public async Task The_document_list_tool_shows_what_is_indexed()
    {
        await Seed();

        var result = await CallAsync(Tools(), "rag_list_documents", new());

        Assert.Equal(2, result.GetProperty("count").GetInt32());
        Assert.Contains(result.GetProperty("documents").EnumerateArray(), d => d.GetProperty("source").GetString() == "security.md");
    }

    [Fact]
    public void An_agent_can_search_and_list_but_has_no_tool_to_write_to_the_index()
    {
        var names = Tools().Select(t => t.Name).OrderBy(n => n).ToList();

        Assert.Equal(["rag_list_documents", "rag_search"], names);
    }

    [Fact]
    public void The_search_tool_tells_the_model_that_results_are_data_and_not_instructions()
    {
        var search = Tools().OfType<AIFunction>().Single(t => t.Name == "rag_search");

        Assert.Contains("not instructions", search.Description);
    }
}

public class RagExpiryTests : RagTestBase
{
    [Fact]
    public async Task Chunks_older_than_the_time_to_live_are_deleted_and_newer_ones_stay()
    {
        var ingestion = Ingestion();
        await ingestion.IngestAsync("old.md", Handbook);
        Clock.Now = Clock.Now.AddDays(45);
        await ingestion.IngestAsync("fresh.md", Handbook);
        Options.TimeToLiveDays = 30;

        await new RagExpiryService(Store.Collection, Options, NullLogger<RagExpiryService>.Instance, Clock).ExpireOnceAsync(default);

        Assert.Equal(["fresh.md"], (await Store.Collection.ListDocumentsAsync()).Select(d => d.Source));
    }

    [Fact]
    public async Task A_time_to_live_of_zero_keeps_everything()
    {
        await Ingestion().IngestAsync("old.md", Handbook);
        Clock.Now = Clock.Now.AddDays(4000);

        await new RagExpiryService(Store.Collection, Options, NullLogger<RagExpiryService>.Instance, Clock).ExpireOnceAsync(default);

        Assert.Single(await Store.Collection.ListDocumentsAsync());
    }
}

public class RagRegistrationTests : RagTestBase
{
    private ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<VectorStoreCollection<string, DocumentRecord>>(Store.Collection);
        services.AddSingleton<IDocumentIndex>(Store.Collection);
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(Embedder);
        services.AddSingleton<IChatClient>(new ScriptedChatClient("[0]"));
        services.AddAiAgentCanvasRag(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void By_default_the_best_passages_are_added_to_every_turn_and_the_tools_are_registered()
    {
        using var provider = Build([]);

        Assert.Single(provider.GetServices<AIContextProvider>());
        Assert.Equal(["rag_list_documents", "rag_search"], provider.GetRequiredService<IReadOnlyList<AITool>>().Select(t => t.Name).OrderBy(n => n));
        Assert.NotNull(provider.GetRequiredService<RagIngestionService>());
    }

    [Fact]
    public void With_auto_inject_off_the_agent_searches_only_by_calling_the_tool()
    {
        using var provider = Build(new() { ["Agent:Rag:AutoInject"] = "false" });

        Assert.Empty(provider.GetServices<AIContextProvider>());
        Assert.Contains(provider.GetRequiredService<IReadOnlyList<AITool>>(), t => t.Name == "rag_search");
    }

    [Fact]
    public void The_chunk_size_comes_from_configuration()
    {
        using var provider = Build(new() { ["Agent:Rag:ChunkSize"] = "300", ["Agent:Rag:ChunkOverlap"] = "30" });

        var chunker = provider.GetRequiredService<DocumentChunker>();
        Assert.Equal(300, chunker.ChunkSize);
        Assert.Equal(30, chunker.ChunkOverlap);
    }

    [Fact]
    public void The_expiry_service_runs_only_when_a_time_to_live_is_set()
    {
        using var off = Build([]);
        using var on = Build(new() { ["Agent:Rag:TimeToLiveDays"] = "30" });

        Assert.DoesNotContain(off.GetServices<Microsoft.Extensions.Hosting.IHostedService>(), s => s is RagExpiryService);
        Assert.Contains(on.GetServices<Microsoft.Extensions.Hosting.IHostedService>(), s => s is RagExpiryService);
    }
}

public class RagEndpointTests : RagTestBase
{
    private async Task<(WebApplication App, HttpClient Client)> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddLogging();
        builder.Services.AddSingleton<IDocumentIndex>(Store.Collection);
        builder.Services.AddSingleton(Ingestion());
        var app = builder.Build();
        app.MapRagEndpoints();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (app, new HttpClient { BaseAddress = new Uri(address) });
    }

    [Fact]
    public async Task A_document_is_indexed_listed_and_deleted_over_http()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var posted = await client.PostAsJsonAsync("/api/rag/documents", new { source = "handbook.md", text = Handbook, tags = "hr" });
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        var result = JsonDocument.Parse(await posted.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, result.GetProperty("version").GetInt32());

        var list = JsonDocument.Parse(await client.GetStringAsync("/api/rag/documents")).RootElement;
        Assert.Equal("handbook.md", list[0].GetProperty("source").GetString());

        var deleted = await client.DeleteAsync("/api/rag/documents?source=handbook.md");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(await client.GetStringAsync("/api/rag/documents")).RootElement.ValueKind);
        Assert.Equal(0, JsonDocument.Parse(await client.GetStringAsync("/api/rag/documents")).RootElement.GetArrayLength());
    }

    [Fact]
    public async Task A_source_with_slashes_can_be_deleted()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;
        await client.PostAsJsonAsync("/api/rag/documents", new { source = "wiki/hr/handbook.md", text = Handbook });

        var deleted = await client.DeleteAsync("/api/rag/documents?source=" + Uri.EscapeDataString("wiki/hr/handbook.md"));

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
    }

    [Fact]
    public async Task A_bad_request_gets_a_400_with_the_reason_and_a_missing_document_a_404()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var noSource = await client.PostAsJsonAsync("/api/rag/documents", new { text = Handbook });
        Assert.Equal(HttpStatusCode.BadRequest, noSource.StatusCode);
        Assert.Contains("Name the source", await noSource.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync("/api/rag/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/rag/documents?source=missing.md")).StatusCode);
    }
}
