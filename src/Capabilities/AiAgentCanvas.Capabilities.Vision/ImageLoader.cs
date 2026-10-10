using System.Net;
using System.Net.Sockets;
using AiAgentCanvas.Abstractions;

namespace AiAgentCanvas.Capabilities.Vision;

public sealed class ImageLoadException(string message) : Exception(message);

/// <summary>
/// Turns a path or an address into image bytes the model can see. It is the only door to
/// the outside for this capability, so it holds the rules: allowlists that deny when empty,
/// a size cap enforced while reading, and the type decided by the file's own first bytes and
/// not by its name or the server's claim.
/// </summary>
public sealed class ImageLoader : IDisposable
{
    private readonly VisionOptions _options;
    private readonly HttpClient _http;

    public ImageLoader(VisionOptions options, HttpMessageHandler? handler = null)
    {
        _options = options;
        _http = new HttpClient(handler ?? CreateHandler(options))
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(1, options.FetchTimeoutSeconds)),
        };
    }

    public async Task<ImageInput> LoadAsync(string source, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ImageLoadException("An image source is empty.");

        source = source.Trim();
        var bytes = Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? await FetchAsync(uri, ct)
            : await ReadFileAsync(source, ct);

        var mediaType = Sniff(bytes)
            ?? throw new ImageLoadException("That is not a PNG, JPEG, GIF or WebP image.");

        return new ImageInput(bytes, mediaType);
    }

    private async Task<byte[]> ReadFileAsync(string path, CancellationToken ct)
    {
        if (_options.AllowedPaths.Count == 0)
            throw new ImageLoadException("Reading image files is off. Set Agent:Vision:AllowedPaths to the folders images may come from.");

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ImageLoadException("That is not a usable path.");
        }

        var info = new FileInfo(full);
        if (!info.Exists)
            throw new ImageLoadException("No image file at that path.");

        // Follow links to the real file, so a link inside an allowed folder cannot lead out of it.
        var real = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? full;

        if (!_options.AllowedPaths.Any(root => IsInside(root, real)))
            throw new ImageLoadException("That path is outside the folders images may be read from.");

        var length = new FileInfo(real).Length;
        if (length > _options.MaxImageBytes)
            throw new ImageLoadException($"The image is {length} bytes. The limit is {_options.MaxImageBytes}.");

        return await File.ReadAllBytesAsync(real, ct);
    }

    private async Task<byte[]> FetchAsync(Uri uri, CancellationToken ct)
    {
        if (_options.AllowedUrlHosts.Count == 0)
            throw new ImageLoadException("Fetching images is off. Set Agent:Vision:AllowedUrlHosts to the hosts images may come from.");

        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new ImageLoadException("Only https addresses are fetched.");

        if (!HostAllowed(_options.AllowedUrlHosts, uri.Host))
            throw new ImageLoadException($"'{uri.Host}' is not one of the hosts images may be fetched from.");

        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new ImageLoadException("An address with a user name or password is not fetched.");

        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
                throw new ImageLoadException($"The server answered {(int)response.StatusCode}.");

            if (response.Content.Headers.ContentLength is { } declared && declared > _options.MaxImageBytes)
                throw new ImageLoadException($"The image is {declared} bytes. The limit is {_options.MaxImageBytes}.");

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > _options.MaxImageBytes)
                    throw new ImageLoadException($"The image is larger than the {_options.MaxImageBytes} byte limit.");
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        catch (HttpRequestException ex)
        {
            throw new ImageLoadException($"The image could not be fetched: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ImageLoadException("The image fetch timed out.");
        }
    }

    internal static bool IsInside(string root, string path)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.StartsWith(rootFull, comparison);
    }

    internal static bool HostAllowed(IEnumerable<string> allowed, string host)
    {
        foreach (var pattern in allowed)
        {
            if (pattern.StartsWith("*.", StringComparison.Ordinal))
            {
                var suffix = pattern[1..];
                if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && host.Length > suffix.Length)
                    return true;
            }
            else if (string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Decides the type from the first bytes. Anything else, including SVG, is refused.</summary>
    internal static string? Sniff(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A)
            return "image/png";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
            return "image/jpeg";
        if (b.Length >= 6 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8' && (b[4] == '7' || b[4] == '9') && b[5] == 'a')
            return "image/gif";
        if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P')
            return "image/webp";
        return null;
    }

    private static SocketsHttpHandler CreateHandler(VisionOptions options) => new()
    {
        AllowAutoRedirect = false,
        ConnectCallback = async (context, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            foreach (var address in addresses)
            {
                if (!options.AllowPrivateNetworks && IsInternal(address))
                    continue;

                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }

            throw new HttpRequestException("The host resolves only to internal addresses, which are not fetched.");
        },
    };

    internal static bool IsInternal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal || address.IsIPv6Multicast;

        var b = address.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 100 && b[1] is >= 64 and <= 127)
            || b[0] == 0
            || b[0] >= 224;
    }

    public void Dispose() => _http.Dispose();
}
