namespace AiAgentCanvas.Abstractions;

public enum ToolStateBehavior
{
    Snapshot,
    Delta,
}

public sealed record ToolStateMapping(string ToolName, ToolStateBehavior Behavior);
