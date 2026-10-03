using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Orchestration.Services;

/// <summary>
/// Daily spend limits for unattended runs, in the currency of <c>Agent:Pricing</c>.
/// A limit of zero means no limit. The window is the last 24 hours.
/// </summary>
public sealed class BudgetOptions
{
    public const string SectionName = "Agent:Budgets";

    /// <summary>Off by default. Turning it on requires the run ledger and model prices.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Ceiling on all unattended spend together, whichever agent or trigger incurs it.</summary>
    public double TotalDailyLimit { get; set; }

    /// <summary>Ceiling for any one agent without an entry in <see cref="Agents"/>.</summary>
    public double DailyLimit { get; set; }

    /// <summary>Agent name to its own daily ceiling.</summary>
    public Dictionary<string, double> Agents { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Trigger id to its own daily ceiling.</summary>
    public Dictionary<string, double> Triggers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A notification is sent once a limit is this fraction used.</summary>
    public double WarnAtFraction { get; set; } = 0.8;

    /// <summary>How long a refused trigger event waits before the guard is asked again.</summary>
    public int DeferMinutes { get; set; } = 60;

    internal double LimitForAgent(string agentName) =>
        Agents.TryGetValue(agentName, out var limit) ? limit : DailyLimit;
}

/// <summary>
/// Refuses to start an unattended run once a limit has been spent, and warns before it
/// gets there. Spend is read from the run ledger, so it survives a restart. A run that
/// is already in flight is not counted until it ends, so concurrent runs can overshoot
/// a limit by their own cost.
/// </summary>
public sealed class BudgetGuard : IBudgetGuard
{
    private readonly IRunLedger _ledger;
    private readonly BudgetOptions _options;
    private readonly INotificationSink? _sink;
    private readonly ILogger<BudgetGuard> _logger;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly HashSet<string> _notified = [];
    private string _notifiedDay = string.Empty;

    public BudgetGuard(
        IRunLedger ledger,
        BudgetOptions options,
        ILogger<BudgetGuard> logger,
        INotificationSink? sink = null,
        TimeProvider? time = null)
    {
        _ledger = ledger;
        _options = options;
        _logger = logger;
        _sink = sink;
        _time = time ?? TimeProvider.System;
    }

    public async Task<BudgetDecision> CheckAsync(string agentName, string? triggerId, CancellationToken ct = default)
    {
        if (!_options.Enabled)
            return BudgetDecision.Allow;

        var since = _time.GetUtcNow().AddHours(-24);
        var retry = TimeSpan.FromMinutes(Math.Max(1, _options.DeferMinutes));

        if (triggerId is not null
            && _options.Triggers.TryGetValue(triggerId, out var triggerLimit) && triggerLimit > 0)
        {
            var spent = _ledger.Totals(since, triggerId: triggerId).EstimatedCost;
            var decision = await EvaluateAsync("trigger", triggerId, spent, triggerLimit, retry, ct);
            if (!decision.Allowed)
                return decision;
        }

        var agentLimit = _options.LimitForAgent(agentName);
        if (agentLimit > 0)
        {
            var spent = _ledger.Totals(since, agentName: agentName).EstimatedCost;
            var decision = await EvaluateAsync("agent", agentName, spent, agentLimit, retry, ct);
            if (!decision.Allowed)
                return decision;
        }

        if (_options.TotalDailyLimit > 0)
        {
            var spent = _ledger.Totals(since).EstimatedCost;
            var decision = await EvaluateAsync("total", "all", spent, _options.TotalDailyLimit, retry, ct);
            if (!decision.Allowed)
                return decision;
        }

        return BudgetDecision.Allow;
    }

    private async Task<BudgetDecision> EvaluateAsync(
        string scope, string key, double spent, double limit, TimeSpan retry, CancellationToken ct)
    {
        if (spent >= limit)
        {
            AgentTelemetry.BudgetDecisions.Add(1,
                new KeyValuePair<string, object?>("scope", scope),
                new KeyValuePair<string, object?>("action", "blocked"));

            var reason = $"The {scope} '{key}' has spent {spent:F2} of its {limit:F2} daily limit. Unattended runs are paused until spend in the last 24 hours drops below it.";
            _logger.LogWarning("Budget reached: {Reason}", reason);

            await NotifyOnceAsync(scope, key, "blocked", $"Spend limit reached: {scope} {key}", reason, ct);
            return new BudgetDecision(false, scope, spent, limit, reason, retry);
        }

        if (_options.WarnAtFraction > 0 && spent >= limit * _options.WarnAtFraction)
        {
            AgentTelemetry.BudgetDecisions.Add(1,
                new KeyValuePair<string, object?>("scope", scope),
                new KeyValuePair<string, object?>("action", "warned"));

            await NotifyOnceAsync(scope, key, "warned", $"Spend nearing limit: {scope} {key}",
                $"The {scope} '{key}' has spent {spent:F2} of its {limit:F2} daily limit.", ct);
        }

        return new BudgetDecision(true, scope, spent, limit);
    }

    /// <summary>One notification per limit and level each day, so a busy queue does not repeat it.</summary>
    private async Task NotifyOnceAsync(string scope, string key, string level, string title, string body, CancellationToken ct)
    {
        if (_sink is null)
            return;

        lock (_gate)
        {
            var day = _time.GetUtcNow().ToString("yyyy-MM-dd");
            if (day != _notifiedDay)
            {
                _notified.Clear();
                _notifiedDay = day;
            }

            if (!_notified.Add($"{scope}:{key}:{level}"))
                return;
        }

        try
        {
            await _sink.SendAsync(new AgentNotification { Title = title, Body = body, Source = $"budget:{scope}:{key}" }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send the budget notification for {Scope} {Key}", scope, key);
        }
    }
}

public static class BudgetServiceExtensions
{
    /// <summary>
    /// Registers the spend guard. Requires <see cref="IRunLedger"/>, because limits are
    /// measured from recorded runs, and requires model prices, because tokens alone
    /// cannot trip a limit expressed in money.
    /// </summary>
    public static IServiceCollection AddAiAgentCanvasBudgets(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new BudgetOptions();
        configuration.GetSection(BudgetOptions.SectionName).Bind(options);

        if (options.Enabled)
        {
            var pricing = new ModelPricingOptions();
            configuration.GetSection(ModelPricingOptions.SectionName).Bind(pricing);

            if (!pricing.Enabled || pricing.Models.Count == 0)
            {
                throw new InvalidOperationException(
                    "Agent:Budgets is enabled but Agent:Pricing has no model rates. Spend would read as zero "
                    + "and no limit could ever trip. Add a rate for each model in use.");
            }
        }

        services.AddSingleton(options);
        services.AddSingleton<IBudgetGuard, BudgetGuard>();
        return services;
    }
}
