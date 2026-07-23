namespace AiAgentCanvas.Abstractions;

public sealed class HandoffResult
{
    public required string Status { get; init; }
    public required string Agent { get; init; }
    public string? Response { get; init; }
    public string? Error { get; init; }
}

public interface IAgentHandoff
{
    Task<HandoffResult> HandoffAsync(string targetAgent, string context, CancellationToken cancellationToken = default);
}
