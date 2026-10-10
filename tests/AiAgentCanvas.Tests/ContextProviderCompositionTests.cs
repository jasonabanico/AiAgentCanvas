#pragma warning disable MEAI001, MAAI001

using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Capabilities.EpisodicMemory;
using AiAgentCanvas.Capabilities.Rag;
using AiAgentCanvas.Orchestration;
using AiAgentCanvas.Orchestration.Skills;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.VectorData;
using Xunit;

namespace AiAgentCanvas.Tests;

/// <summary>
/// The agent merges what each context provider returns into the instructions, messages and
/// tools it already has. A provider that returns the incoming context, which already holds
/// everything the earlier providers added, adds all of it a second time. With N providers the
/// prompt grew by a factor of about 2 to the N: a host with a few features on sent a 42,000
/// token system prompt, tool lists of hundreds of copies, and the same user message several
/// times, and the prompt budget then cut the tail, which is where retrieved documents and
/// memories sit. These tests pin the contract and the real providers to it.
/// </summary>
public class ContextProviderCompositionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ctx-{Guid.NewGuid():N}");

    public ContextProviderCompositionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    // ---- the contract, with minimal providers ----

    private abstract class AddsTag(string tag) : AIContextProvider
    {
        protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken) =>
            new(new AIContext
            {
                Instructions = $"[{tag}]",
                Tools = [AIFunctionFactory.Create(() => "x", $"tool_{tag}", "A tool.")],
            });
    }

    private sealed class First() : AddsTag("first");
    private sealed class Second() : AddsTag("second");
    private sealed class Third() : AddsTag("third");
    private sealed class Fourth() : AddsTag("fourth");
    private sealed class Fifth() : AddsTag("fifth");
    private sealed class Sixth() : AddsTag("sixth");

    private static async Task<(ChatOptions Options, List<ChatMessage> Messages)> RunAsync(params AIContextProvider[] providers)
    {
        var model = new ScriptedChatClient("ok");
        var agent = new ChatClientAgent(model, new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions { Instructions = "base" },
            AIContextProviders = providers,
        });

        await agent.RunAsync("hello");
        return (model.Options[0]!, model.Calls[0]);
    }

    private static int Count(string text, string part)
    {
        var count = 0;
        for (var i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    [Fact]
    public async Task Providers_that_return_only_their_addition_put_each_block_in_the_prompt_once()
    {
        var (options, messages) = await RunAsync(new First(), new Second(), new Third(), new Fourth(), new Fifth(), new Sixth());

        foreach (var tag in new[] { "first", "second", "third", "fourth", "fifth", "sixth" })
        {
            Assert.Equal(1, Count(options.Instructions!, $"[{tag}]"));
            Assert.Single(options.Tools!, t => t.Name == $"tool_{tag}");
        }
        Assert.Equal(1, Count(options.Instructions!, "base"));
        Assert.Single(messages, m => m.Role == ChatRole.User);
    }

    // ---- the real providers, on the real default agent ----

    private sealed class Rig : IDisposable
    {
        public ScriptedChatClient Model { get; } = new("fine");
        public ServiceProvider Services { get; }
        public TempVectorStore Vectors { get; } = new();

        public Rig(string root, Func<EpisodicMemoryStore, Task>? seedMemory = null, bool rag = true)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IChatClient>(Model);
            services.AddSingleton<IReadOnlyList<AITool>>([AIFunctionFactory.Create(() => "ok", "startup_tool", "A tool registered at startup.")]);

            services.AddAiAgentCanvasEpisodicMemory(Path.Combine(root, "episodes.db"));

            if (rag)
            {
                services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new HashEmbeddingGenerator());
                services.AddSingleton<VectorStoreCollection<string, DocumentRecord>>(Vectors.Collection);
                services.AddSingleton<IDocumentIndex>(Vectors.Collection);
                services.AddAiAgentCanvasRag(new ConfigurationBuilder().Build());
            }

            services.AddAiAgentCanvas(new ConfigurationBuilder().Build());
            services.AddAiAgentCanvasInterAgentCommunication(_ => _ => null, _ => () => []);

            Services = services.BuildServiceProvider();
            Services.GetRequiredService<DynamicToolRegistry>().Register("connector:x", [AIFunctionFactory.Create(() => "ok", "runtime_tool", "A tool added while running.")]);

            if (seedMemory is not null)
                seedMemory(Services.GetRequiredService<EpisodicMemoryStore>()).GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Services.Dispose();
            Vectors.Dispose();
        }
    }

    [Fact]
    public async Task The_default_agent_with_memory_retrieval_and_runtime_tools_sends_each_block_once()
    {
        using var rig = new Rig(_root, seedMemory: store =>
        {
            store.Save(new Episode { AgentName = "AiAgentCanvas", Goal = "rotate the signing keys", Summary = "done in two steps", Outcome = "success", Importance = 0.9 });
            return Task.CompletedTask;
        });
        await rig.Services.GetRequiredService<RagIngestionService>().IngestAsync(
            "handbook.md", "Employees accrue vacation at two days per month of service.", "hr");
        var agent = rig.Services.GetRequiredService<AIAgent>();

        await agent.RunAsync("How many vacation days do I accrue each month?");

        var options = rig.Model.Options[^1]!;
        var instructions = options.Instructions!;

        Assert.Equal(1, Count(instructions, "## Episodic Memory"));
        Assert.Equal(1, Count(instructions, "rotate the signing keys"));
        Assert.Equal(1, Count(instructions, "Relevant context from documents"));
        Assert.Equal(1, Count(instructions, "Employees accrue vacation"));

        var names = options.Tools!.Select(t => t.Name).ToList();
        Assert.Equal(names.Distinct().Count(), names.Count);
        Assert.Contains("startup_tool", names);
        Assert.Contains("runtime_tool", names);
        Assert.Contains("rag_search", names);

        // The agent adds a todo-list reminder of its own, but the question is sent once.
        Assert.Single(rig.Model.Calls[^1], m => m.Role == ChatRole.User && m.Text.StartsWith("How many vacation days", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_prompt_does_not_grow_with_the_number_of_providers()
    {
        using var withTwo = new Rig(_root + "-a", rag: false);
        using var withThree = new Rig(_root + "-b", rag: true);
        Directory.CreateDirectory(_root + "-a");
        Directory.CreateDirectory(_root + "-b");

        await withTwo.Services.GetRequiredService<AIAgent>().RunAsync("hello");
        await withThree.Services.GetRequiredService<AIAgent>().RunAsync("hello");

        var small = withTwo.Model.Options[^1]!.Instructions!.Length;
        var large = withThree.Model.Options[^1]!.Instructions!.Length;

        // Adding a provider adds its own few lines. It does not double what was already there.
        Assert.True(large < small * 1.5 + 200, $"{small} characters grew to {large} when one provider was added.");

        try { Directory.Delete(_root + "-a", true); Directory.Delete(_root + "-b", true); } catch (IOException) { }
    }

    [Fact]
    public async Task The_default_system_prompt_appears_once()
    {
        using var rig = new Rig(_root, rag: false);

        await rig.Services.GetRequiredService<AIAgent>().RunAsync("hello");

        var instructions = rig.Model.Options[^1]!.Instructions!;
        Assert.Equal(1, Count(instructions, "Use the available tools to help answer questions."));
    }
}
