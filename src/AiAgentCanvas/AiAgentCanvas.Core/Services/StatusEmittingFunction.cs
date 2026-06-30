using System.Threading.Channels;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Core.Services;

public sealed class ToolStatusEvent
{
    public string ToolName { get; init; } = "";
    public bool IsComplete { get; init; }
}

public sealed class ToolStatusChannel
{
    private readonly Channel<ToolStatusEvent> _channel = Channel.CreateUnbounded<ToolStatusEvent>();
    public ChannelWriter<ToolStatusEvent> Writer => _channel.Writer;
    public ChannelReader<ToolStatusEvent> Reader => _channel.Reader;
}

internal sealed class StatusEmittingFunction : DelegatingAIFunction
{
    private readonly ToolStatusChannel _statusChannel;

    public StatusEmittingFunction(AIFunction inner, ToolStatusChannel statusChannel)
        : base(inner)
    {
        _statusChannel = statusChannel;
    }

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        await _statusChannel.Writer.WriteAsync(
            new ToolStatusEvent { ToolName = InnerFunction.Name, IsComplete = false },
            cancellationToken);

        var result = await base.InvokeCoreAsync(arguments, cancellationToken);

        await _statusChannel.Writer.WriteAsync(
            new ToolStatusEvent { ToolName = InnerFunction.Name, IsComplete = true },
            cancellationToken);

        return result;
    }
}
