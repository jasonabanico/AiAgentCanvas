namespace AiAgentCanvas.Capabilities.McpServer;

public sealed class McpServerOptions
{
    public const string SectionName = "Agent:McpServer";

    /// <summary>The route the MCP endpoint is served on.</summary>
    public string Path { get; set; } = "/mcp";

    public string ServerName { get; set; } = "AiAgentCanvas";

    /// <summary>
    /// The tools an outside client may call, by name. Empty exposes nothing. A trailing
    /// <c>*</c> matches a family, such as <c>rag_*</c>. Only tools the host registers are
    /// offered. Connector tools, which come and go as connections change, are not.
    /// </summary>
    public List<string> ExposedTools { get; set; } = [];

    /// <summary>
    /// The endpoint hands the agent's tools to whoever can reach it, so the host refuses to
    /// start with it on and authentication off. Set this to true to accept that, for example
    /// when the endpoint is only reachable from a private network.
    /// </summary>
    public bool AllowUnauthenticated { get; set; } = false;

    /// <summary>A longer tool result is cut to this many characters before it leaves.</summary>
    public int MaxResultChars { get; set; } = 100_000;
}
