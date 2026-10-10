namespace AiAgentCanvas.Capabilities.Vision;

public sealed class VisionOptions
{
    public const string SectionName = "Agent:Vision";

    /// <summary>
    /// Folders an image may be read from. Empty means no file may be read, the same rule
    /// the system tools follow. A path outside these folders is refused, links included.
    /// </summary>
    public List<string> AllowedPaths { get; set; } = [];

    /// <summary>
    /// Hosts an image may be fetched from: an exact name or <c>*.example.com</c>. Empty means
    /// no address may be fetched. Only https is used.
    /// </summary>
    public List<string> AllowedUrlHosts { get; set; } = [];

    /// <summary>
    /// A fetch to a private, loopback or link-local address is refused unless this is true.
    /// The check runs on the address actually connected to, so a name that resolves to an
    /// internal address does not get past it.
    /// </summary>
    public bool AllowPrivateNetworks { get; set; } = false;

    public int MaxImageBytes { get; set; } = 5 * 1024 * 1024;

    public int MaxImagesPerCall { get; set; } = 4;

    public int FetchTimeoutSeconds { get; set; } = 15;
}
