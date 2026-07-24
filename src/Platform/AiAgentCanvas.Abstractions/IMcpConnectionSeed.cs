namespace AiAgentCanvas.Abstractions;

public interface IMcpConnectionSeed
{
    string Name { get; }
    string Endpoint { get; }
    string Transport { get; }
}

public sealed class McpConnectionSeed : IMcpConnectionSeed
{
    public string Name { get; }
    public string Endpoint { get; }
    public string Transport { get; }

    public McpConnectionSeed(string name, string endpoint, string transport)
    {
        Name = name;
        Endpoint = endpoint;
        Transport = transport;
    }
}
