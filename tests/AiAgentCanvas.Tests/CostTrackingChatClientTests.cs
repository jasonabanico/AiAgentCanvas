using System.Diagnostics.Metrics;
using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Orchestration.Services;
using Microsoft.Extensions.AI;
using Xunit;

namespace AiAgentCanvas.Tests;

public class CostTrackingChatClientTests
{
    private static ModelPricingOptions Pricing() => new()
    {
        Enabled = true,
        Currency = "USD",
        DefaultModel = "gpt-4o",
        Models =
        {
            ["gpt-4o"] = new ModelRate { InputPer1M = 2.50, OutputPer1M = 10.00 },
            ["gpt-4o-mini"] = new ModelRate { InputPer1M = 0.15, OutputPer1M = 0.60 },
        },
    };

    [Fact]
    public void Rate_lookup_is_case_insensitive()
    {
        var rate = Pricing().RateFor("GPT-4O-MINI");
        Assert.NotNull(rate);
        Assert.Equal(0.15, rate!.InputPer1M);
    }

    [Fact]
    public void Unknown_model_falls_back_to_the_default_rate()
    {
        var rate = Pricing().RateFor("some-databricks-endpoint");
        Assert.NotNull(rate);
        Assert.Equal(2.50, rate!.InputPer1M);
    }

    [Fact]
    public void Unknown_model_with_no_default_has_no_rate()
    {
        var pricing = Pricing();
        pricing.DefaultModel = null;
        Assert.Null(pricing.RateFor("mystery-model"));
    }

    [Fact]
    public async Task Records_tokens_and_cost_for_a_non_streaming_call()
    {
        var (tokens, cost) = await CaptureAsync(streaming: false);

        // 1000 in at 2.50/1M and 500 out at 10.00/1M.
        Assert.Equal(1000, tokens["input"]);
        Assert.Equal(500, tokens["output"]);
        Assert.Equal(0.0025, cost["input"], 9);
        Assert.Equal(0.005, cost["output"], 9);
    }

    [Fact]
    public async Task Records_tokens_and_cost_for_a_streaming_call()
    {
        // Streaming reports usage on a late update, so this is the path that
        // previously escaped accounting entirely.
        var (tokens, cost) = await CaptureAsync(streaming: true);

        Assert.Equal(1000, tokens["input"]);
        Assert.Equal(500, tokens["output"]);
        Assert.Equal(0.0075, cost.Values.Sum(), 9);
    }

    [Fact]
    public async Task Counts_tokens_but_no_cost_when_pricing_is_disabled()
    {
        var pricing = Pricing();
        pricing.Enabled = false;

        var (tokens, cost) = await CaptureAsync(streaming: false, pricing: pricing);

        Assert.Equal(1000, tokens["input"]);
        Assert.Empty(cost);
    }

    /// <summary>
    /// Drives one call through the client with a MeterListener attached, and returns
    /// the token and cost measurements keyed by direction.
    /// </summary>
    private static async Task<(Dictionary<string, long> Tokens, Dictionary<string, double> Cost)> CaptureAsync(
        bool streaming, ModelPricingOptions? pricing = null)
    {
        var tokens = new Dictionary<string, long>();
        var cost = new Dictionary<string, double>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == AgentTelemetry.MeterName
                && instrument.Name is "aiagentcanvas.model.tokens" or "aiagentcanvas.model.cost")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (instrument.Name == "aiagentcanvas.model.tokens")
                tokens[Direction(tags)] = tokens.GetValueOrDefault(Direction(tags)) + value;
        });
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            if (instrument.Name == "aiagentcanvas.model.cost")
                cost[Direction(tags)] = cost.GetValueOrDefault(Direction(tags)) + value;
        });
        listener.Start();

        var usage = new UsageDetails { InputTokenCount = 1000, OutputTokenCount = 500 };
        var inner = new UsageReportingChatClient("gpt-4o", usage);
        var client = new CostTrackingChatClient(inner, pricing ?? Pricing());

        if (streaming)
        {
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
            {
            }
        }
        else
        {
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);
        }

        listener.RecordObservableInstruments();
        return (tokens, cost);
    }

    private static string Direction(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == "direction")
                return tag.Value?.ToString() ?? "";
        }
        return "";
    }

    /// <summary>Returns a fixed usage block, on the response and on a streaming update.</summary>
    private sealed class UsageReportingChatClient : IChatClient
    {
        private readonly string _modelId;
        private readonly UsageDetails _usage;

        public UsageReportingChatClient(string modelId, UsageDetails usage)
        {
            _modelId = modelId;
            _usage = usage;
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
            {
                ModelId = _modelId,
                Usage = _usage,
            });

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok") { ModelId = _modelId };
            yield return new ChatResponseUpdate { Contents = [new UsageContent(_usage)] };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
