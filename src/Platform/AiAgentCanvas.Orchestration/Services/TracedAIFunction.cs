using System.Diagnostics;
using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Orchestration.Services;

/// <summary>
/// Opens a span and records a counter and duration for every tool invocation.
/// Logs alone say a tool was called; a span says which run it belonged to and what
/// it cost, which is what makes a semantic failure traceable after the fact.
/// </summary>
public sealed class TracedAIFunction : DelegatingAIFunction
{
    public TracedAIFunction(AIFunction inner) : base(inner)
    {
    }

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var toolName = InnerFunction.Name;
        using var activity = AgentTelemetry.Source.StartActivity($"tool {toolName}", ActivityKind.Internal);
        activity?.SetTag("tool.name", toolName);
        activity?.SetTag("tool.argument_count", arguments.Count);

        var started = Stopwatch.GetTimestamp();
        var outcome = "ok";

        try
        {
            var result = await base.InvokeCoreAsync(arguments, cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
            throw;
        }
        catch (Exception ex)
        {
            outcome = "error";
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("error.type", ex.GetType().Name);
            throw;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var tags = new TagList
            {
                { "tool.name", toolName },
                { "outcome", outcome },
            };

            AgentTelemetry.ToolCalls.Add(1, tags);
            AgentTelemetry.ToolDuration.Record(elapsed, tags);
            activity?.SetTag("tool.duration_ms", elapsed);
        }
    }
}
