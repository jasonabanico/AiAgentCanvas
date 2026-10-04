using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Capabilities.RunLedger;
using AiAgentCanvas.Orchestration;
using AiAgentCanvas.Orchestration.Services;
using Microsoft.Agents.AI;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AiAgentCanvas.Tests;

public class AgentPipelineTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pipeline-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + suffix); }
            catch (IOException) { }
        }
    }

    private sealed class UsageChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "hello"))
            {
                ModelId = "test-model",
                Usage = new UsageDetails { InputTokenCount = 1_000_000, OutputTokenCount = 100_000 },
            });

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "hello") { ModelId = response.ModelId };
            yield return new ChatResponseUpdate { Contents = [new UsageContent(response.Usage!)] };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private static ServiceProvider BuildServices()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agent:Pricing:Models:test-model:InputPer1M"] = "2",
            ["Agent:Pricing:Models:test-model:OutputPer1M"] = "10",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IChatClient>(new UsageChatClient());
        services.AddAiAgentCanvas(config);
        services.AddAiAgentCanvasInterAgentCommunication(
            _ => name => name == "helper"
                ? new AgentPersonaInfo { Name = "helper", Description = "a helper", Instructions = "help" }
                : null,
            _ => () => [new AgentPersonaInfo { Name = "helper", Description = "a helper", Instructions = "help" }]);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task A_persona_agent_runs_on_the_guarded_pipeline_so_its_spend_is_counted()
    {
        await using var sp = BuildServices();
        var ledger = new SqliteRunLedger(_dbPath);
        var agent = sp.GetRequiredService<AgentRegistry>().Resolve("helper");
        Assert.NotNull(agent);

        await RunTracking.RunAsync(ledger, new RunStart(RunSource.Handoff, "helper", "hi"), async ct =>
        {
            var session = await agent!.CreateSessionAsync(ct);
            var response = await agent.RunAsync([new ChatMessage(ChatRole.User, "hi")], session, cancellationToken: ct);
            return response.Text ?? string.Empty;
        });

        var run = ledger.Recent(new RunQuery()).Single();
        Assert.True(run.ModelCalls >= 1, "the persona agent's model call was not seen by cost tracking");
        Assert.Equal(1_000_000, run.InputTokens);
        Assert.Equal(3.0, run.EstimatedCost, 6);
    }

    [Fact]
    public void The_pipeline_is_one_shared_client_for_the_default_and_persona_agents()
    {
        using var sp = BuildServices();

        var first = sp.GetRequiredKeyedService<IChatClient>(AgentClientKeys.Pipeline);
        var second = sp.GetRequiredKeyedService<IChatClient>(AgentClientKeys.Pipeline);

        Assert.Same(first, second);
    }

    [Fact]
    public void Wrapped_tools_are_traced_and_pass_through_when_not_functions()
    {
        using var sp = BuildServices();
        var function = AIFunctionFactory.Create(() => "x", "ping");

        var wrapped = AgentPipeline.WrapTools(sp, [function]);

        Assert.IsType<TracedAIFunction>(Assert.Single(wrapped));
        Assert.Equal("ping", wrapped[0].Name);
    }
}
