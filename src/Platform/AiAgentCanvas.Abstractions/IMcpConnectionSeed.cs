namespace AiAgentCanvas.Abstractions;

public interface IMcpConnectionSeed
{
    string Name { get; }
    string Endpoint { get; }
    string Transport { get; }
    string? BearerToken { get; }
    string? ApiKey { get; }
    string? ExpectedIssuer { get; }
    IDictionary<string, string>? AdditionalHeaders { get; }
}

public sealed class McpConnectionSeed : IMcpConnectionSeed
{
    public string Name { get; }
    public string Endpoint { get; }
    public string Transport { get; }
    public string? BearerToken { get; }
    public string? ApiKey { get; }
    public string? ExpectedIssuer { get; }
    public IDictionary<string, string>? AdditionalHeaders { get; }

    public McpConnectionSeed(
        string name,
        string endpoint,
        string transport,
        string? bearerToken = null,
        string? apiKey = null,
        string? expectedIssuer = null,
        IDictionary<string, string>? additionalHeaders = null)
    {
        Name = name;
        Endpoint = endpoint;
        Transport = transport;
        BearerToken = bearerToken;
        ApiKey = apiKey;
        ExpectedIssuer = expectedIssuer;
        AdditionalHeaders = additionalHeaders;
    }
}
