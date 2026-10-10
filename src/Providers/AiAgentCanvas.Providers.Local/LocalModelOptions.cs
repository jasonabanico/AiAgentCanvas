using System.Net;
using System.Net.Sockets;

namespace AiAgentCanvas.Providers.Local;

/// <summary>
/// Configuration for a model server on the same machine or the same network: Ollama,
/// llama.cpp's <c>llama-server</c>, LM Studio, vLLM or anything else that speaks the
/// OpenAI wire protocol. No account, key or outside network is involved.
/// </summary>
public sealed class LocalModelOptions
{
    public const string SectionName = "Local";

    /// <summary>
    /// Base URL of the OpenAI-compatible API, including the <c>/v1</c> segment. The default
    /// is Ollama's. LM Studio uses <c>http://localhost:1234/v1</c>, llama-server
    /// <c>http://localhost:8080/v1</c> and vLLM <c>http://localhost:8000/v1</c>.
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:11434/v1";

    /// <summary>Most local servers ignore the key, but the client requires a value.</summary>
    public string ApiKey { get; set; } = "not-needed";

    /// <summary>The model tag the server knows, for example <c>gpt-oss:120b</c>.</summary>
    public string? ModelName { get; set; }

    /// <summary>Embedding model for RAG and episodic memory, for example <c>nomic-embed-text</c>. Unset disables embeddings.</summary>
    public string? EmbeddingModelName { get; set; }

    /// <summary>A smaller model for the cost router and history summaries.</summary>
    public string? EconomyModelName { get; set; }

    /// <summary>A model that grades evaluation output. Use a different one from <see cref="ModelName"/>.</summary>
    public string? JudgeModelName { get; set; }

    /// <summary>
    /// The provider refuses an endpoint outside the local machine and private networks, because
    /// a host that is meant to be isolated should fail loudly when a typo points it at the
    /// internet. Set this to true to allow a public address.
    /// </summary>
    public bool AllowNonLocalEndpoint { get; set; }

    /// <summary>Local models answer slowly on long prompts, so the default is longer than a hosted API's.</summary>
    public int RequestTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// True for loopback, private (RFC 1918), link-local and carrier-grade NAT addresses (which
    /// Tailscale uses), for single-label host names, and for the <c>.local</c>, <c>.lan</c>,
    /// <c>.internal</c> and <c>.home.arpa</c> suffixes.
    /// </summary>
    public static bool IsLocalAddress(Uri endpoint)
    {
        var host = endpoint.Host.Trim('[', ']');

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var ip))
            return IPAddress.IsLoopback(ip) || IsPrivate(ip);

        if (!host.Contains('.'))
            return true;

        string[] suffixes = [".local", ".lan", ".internal", ".home.arpa"];
        return suffixes.Any(s => host.EndsWith(s, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (ip.GetAddressBytes()[0] & 0xFE) == 0xFC;

        var b = ip.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 100 && b[1] is >= 64 and <= 127);
    }
}
