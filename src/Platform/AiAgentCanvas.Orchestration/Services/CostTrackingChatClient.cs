using System.Runtime.CompilerServices;
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Orchestration.Services;

/// <summary>Per-million-token rates for one model.</summary>
public sealed class ModelRate
{
    public double InputPer1M { get; set; }
    public double OutputPer1M { get; set; }
    public double CachedInputPer1M { get; set; }
}

/// <summary>
/// Price list keyed by model id. Rates are deployment-specific and change, so they
/// are configuration rather than constants, and an unpriced model still reports its
/// token counts.
/// </summary>
public sealed class ModelPricingOptions
{
    public const string SectionName = "Agent:Pricing";

    public bool Enabled { get; set; } = true;

    /// <summary>Currency label applied to the cost metric. Rates must all be in it.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Model id to rate. The key is matched against the model the provider reports,
    /// case-insensitively, then against <see cref="DefaultModel"/>.
    /// </summary>
    public Dictionary<string, ModelRate> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Rate applied when the reported model has no entry in <see cref="Models"/>.</summary>
    public string? DefaultModel { get; set; }

    public ModelRate? RateFor(string? modelId)
    {
        if (!string.IsNullOrWhiteSpace(modelId) && Models.TryGetValue(modelId, out var rate))
            return rate;

        if (!string.IsNullOrWhiteSpace(DefaultModel) && Models.TryGetValue(DefaultModel, out var fallback))
            return fallback;

        return null;
    }
}

/// <summary>
/// Records tokens and estimated spend for every model call, streaming included.
/// The audit log already stored a token count, but only when that capability was
/// enabled, never as a metric, and with no price applied. A run that loops for an
/// hour showed up as latency and never as money.
/// </summary>
public sealed class CostTrackingChatClient : DelegatingChatClient
{
    private readonly ModelPricingOptions _pricing;
    private readonly ILogger? _logger;

    public CostTrackingChatClient(
        IChatClient inner,
        ModelPricingOptions pricing,
        ILogger? logger = null) : base(inner)
    {
        _pricing = pricing;
        _logger = logger;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken);
            Record(response.ModelId ?? options?.ModelId, response.Usage, "success");
            return response;
        }
        catch (Exception)
        {
            AgentTelemetry.ModelCalls.Add(1,
                new KeyValuePair<string, object?>("model", options?.ModelId ?? "unknown"),
                new KeyValuePair<string, object?>("outcome", "error"));
            throw;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Usage arrives on a late update rather than per chunk, so the totals are
        // accumulated and recorded once the stream completes.
        UsageDetails? usage = null;
        string? modelId = null;

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            modelId ??= update.ModelId;

            foreach (var content in update.Contents.OfType<UsageContent>())
                usage = Merge(usage, content.Details);

            yield return update;
        }

        Record(modelId ?? options?.ModelId, usage, "success");
    }

    private static UsageDetails Merge(UsageDetails? running, UsageDetails incoming)
    {
        if (running is null) return incoming;

        running.InputTokenCount = (running.InputTokenCount ?? 0) + (incoming.InputTokenCount ?? 0);
        running.OutputTokenCount = (running.OutputTokenCount ?? 0) + (incoming.OutputTokenCount ?? 0);
        running.TotalTokenCount = (running.TotalTokenCount ?? 0) + (incoming.TotalTokenCount ?? 0);
        return running;
    }

    private void Record(string? modelId, UsageDetails? usage, string outcome)
    {
        var model = string.IsNullOrWhiteSpace(modelId) ? "unknown" : modelId;

        AgentTelemetry.ModelCalls.Add(1,
            new KeyValuePair<string, object?>("model", model),
            new KeyValuePair<string, object?>("outcome", outcome));

        var run = AgentRunContext.Current;

        if (usage is null)
        {
            run?.AddModelUsage(0, 0, 0);
            return;
        }

        var input = usage.InputTokenCount ?? 0;
        var output = usage.OutputTokenCount ?? 0;

        if (input > 0)
            AgentTelemetry.ModelTokens.Add(input, Tags(model, "input"));
        if (output > 0)
            AgentTelemetry.ModelTokens.Add(output, Tags(model, "output"));

        var rate = _pricing.Enabled ? _pricing.RateFor(modelId) : null;
        if (rate is null)
        {
            // Counted but unpriced. Better than reporting zero spend, which reads as free.
            if (_pricing.Enabled)
                _logger?.LogDebug("No rate configured for model {Model}, tokens counted without cost", model);
            run?.AddModelUsage(input, output, 0);
            return;
        }

        var inputCost = input / 1_000_000d * rate.InputPer1M;
        var outputCost = output / 1_000_000d * rate.OutputPer1M;

        if (inputCost > 0)
            AgentTelemetry.ModelCost.Add(inputCost, Tags(model, "input", _pricing.Currency));
        if (outputCost > 0)
            AgentTelemetry.ModelCost.Add(outputCost, Tags(model, "output", _pricing.Currency));

        run?.AddModelUsage(input, output, inputCost + outputCost);

        _logger?.LogDebug(
            "Model {Model}: {Input} in, {Output} out, about {Cost} {Currency}",
            model, input, output, (inputCost + outputCost).ToString("F6"), _pricing.Currency);
    }

    private static KeyValuePair<string, object?>[] Tags(string model, string direction) =>
        [new("model", model), new("direction", direction)];

    private static KeyValuePair<string, object?>[] Tags(string model, string direction, string currency) =>
        [new("model", model), new("direction", direction), new("currency", currency)];
}
