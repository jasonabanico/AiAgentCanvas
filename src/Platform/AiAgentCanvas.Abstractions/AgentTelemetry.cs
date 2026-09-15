using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AiAgentCanvas.Abstractions;

/// <summary>
/// Single source of truth for the traces and metrics the platform emits. The Host
/// registers <see cref="ActivitySourceName"/> and <see cref="MeterName"/> with
/// OpenTelemetry, so anything recorded here is exported without further wiring.
/// </summary>
public static class AgentTelemetry
{
    public const string ActivitySourceName = "AiAgentCanvas";
    public const string MeterName = "AiAgentCanvas";

    public static readonly ActivitySource Source = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);

    /// <summary>Tool invocations, tagged with tool name and outcome.</summary>
    public static readonly Counter<long> ToolCalls =
        Meter.CreateCounter<long>("aiagentcanvas.tool.calls", "{call}", "Tool invocations by name and outcome");

    /// <summary>Wall-clock duration of a tool invocation.</summary>
    public static readonly Histogram<double> ToolDuration =
        Meter.CreateHistogram<double>("aiagentcanvas.tool.duration", "ms", "Tool invocation duration");

    /// <summary>Agent runs that reached a terminal state, tagged with the reason.</summary>
    public static readonly Counter<long> RunTerminations =
        Meter.CreateCounter<long>("aiagentcanvas.run.terminations", "{run}", "Agent runs by termination reason");

    /// <summary>Tool rounds observed in a single run, recorded when the run ends.</summary>
    public static readonly Histogram<int> RunToolRounds =
        Meter.CreateHistogram<int>("aiagentcanvas.run.tool_rounds", "{round}", "Tool rounds per agent run");

    /// <summary>Prompt tokens sent, broken down by context component.</summary>
    public static readonly Histogram<int> ContextTokens =
        Meter.CreateHistogram<int>("aiagentcanvas.context.tokens", "{token}", "Prompt tokens by context component");

    /// <summary>Context compactions triggered by the budget enforcer.</summary>
    public static readonly Counter<long> ContextCompactions =
        Meter.CreateCounter<long>("aiagentcanvas.context.compactions", "{compaction}", "Context compactions by strategy");

    /// <summary>Evaluation cases run, tagged with category and pass/fail.</summary>
    public static readonly Counter<long> EvalCases =
        Meter.CreateCounter<long>("aiagentcanvas.eval.cases", "{case}", "Evaluation cases by category and outcome");
}

/// <summary>
/// Service keys for the optional secondary chat clients. A provider registers these
/// only when the matching model is configured; consumers resolve them with
/// <c>GetKeyedService</c> and degrade when they are absent.
/// </summary>
public static class AgentClientKeys
{
    /// <summary>Cheaper model used by the cost-aware router for low-complexity turns.</summary>
    public const string Economy = "economy";

    /// <summary>Independent model used to grade output, keeping maker and checker separate.</summary>
    public const string Judge = "judge";
}
