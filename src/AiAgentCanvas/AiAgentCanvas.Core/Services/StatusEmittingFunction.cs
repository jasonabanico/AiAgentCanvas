using System.Threading.Channels;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Core.Services;

public sealed class ToolStatusEvent
{
    public string ToolName { get; init; } = "";
    public bool IsComplete { get; init; }
}

public sealed class ToolStatusBroker
{
    private volatile Channel<ToolStatusEvent>? _current;

    public Channel<ToolStatusEvent> Start()
    {
        var channel = Channel.CreateUnbounded<ToolStatusEvent>();
        _current = channel;
        return channel;
    }

    public void Stop() => _current = null;

    internal Channel<ToolStatusEvent>? Current => _current;
}

internal sealed class StatusEmittingFunction : DelegatingAIFunction
{
    private readonly ToolStatusBroker _broker;

    public StatusEmittingFunction(AIFunction inner, ToolStatusBroker broker)
        : base(inner)
    {
        _broker = broker;
    }

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var channel = _broker.Current;
        if (channel is not null)
        {
            await channel.Writer.WriteAsync(
                new ToolStatusEvent { ToolName = InnerFunction.Name, IsComplete = false },
                cancellationToken);
        }

        var result = await base.InvokeCoreAsync(arguments, cancellationToken);

        if (channel is not null)
        {
            await channel.Writer.WriteAsync(
                new ToolStatusEvent { ToolName = InnerFunction.Name, IsComplete = true },
                cancellationToken);
        }

        return result;
    }
}
