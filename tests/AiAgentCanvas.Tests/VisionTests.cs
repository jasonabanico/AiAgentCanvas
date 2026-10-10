using System.Net;
using System.Text.Json;
using AiAgentCanvas.Abstractions;
using AiAgentCanvas.Capabilities.StructuredOutput;
using AiAgentCanvas.Capabilities.Vision;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiAgentCanvas.Tests;

internal static class Images
{
    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
    public static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10];
    public static readonly byte[] Gif = "GIF89a...."u8.ToArray();
    public static readonly byte[] Webp = "RIFF\0\0\0\0WEBPVP8 "u8.ToArray();
    public static readonly byte[] Svg = "<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"u8.ToArray();
}

public class ImageLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vision-{Guid.NewGuid():N}");
    private readonly string _outside = Path.Combine(Path.GetTempPath(), $"vision-out-{Guid.NewGuid():N}");

    public ImageLoaderTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        Directory.Delete(_root, true);
        Directory.Delete(_outside, true);
    }

    private ImageLoader Loader(Action<VisionOptions>? configure = null, HttpMessageHandler? handler = null)
    {
        var options = new VisionOptions { AllowedPaths = [_root] };
        configure?.Invoke(options);
        return new ImageLoader(options, handler ?? new StubHttp(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
    }

    private sealed class StubHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requested.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    // ---- files ----

    [Theory]
    [InlineData("png", "image/png")]
    [InlineData("jpg", "image/jpeg")]
    [InlineData("gif", "image/gif")]
    [InlineData("webp", "image/webp")]
    public async Task An_image_in_an_allowed_folder_is_read_and_typed_by_its_bytes(string kind, string mediaType)
    {
        var bytes = kind switch { "png" => Images.Png, "jpg" => Images.Jpeg, "gif" => Images.Gif, _ => Images.Webp };
        // The extension is deliberately wrong: the bytes decide.
        var path = Path.Combine(_root, "picture.txt");
        File.WriteAllBytes(path, bytes);

        var image = await Loader().LoadAsync(path);

        Assert.Equal(mediaType, image.MediaType);
        Assert.Equal(bytes, image.Data.ToArray());
    }

    [Fact]
    public async Task With_no_allowed_folders_no_file_is_read()
    {
        var path = Path.Combine(_root, "a.png");
        File.WriteAllBytes(path, Images.Png);

        var ex = await Assert.ThrowsAsync<ImageLoadException>(() => Loader(o => o.AllowedPaths = []).LoadAsync(path));

        Assert.Contains("AllowedPaths", ex.Message);
    }

    [Fact]
    public async Task A_file_outside_the_allowed_folders_is_refused()
    {
        var path = Path.Combine(_outside, "secret.png");
        File.WriteAllBytes(path, Images.Png);

        var ex = await Assert.ThrowsAsync<ImageLoadException>(() => Loader().LoadAsync(path));

        Assert.Contains("outside", ex.Message);
    }

    [Fact]
    public async Task A_dot_dot_path_cannot_climb_out_of_the_allowed_folder()
    {
        File.WriteAllBytes(Path.Combine(_outside, "x.png"), Images.Png);
        var sneaky = Path.Combine(_root, "..", Path.GetFileName(_outside), "x.png");

        await Assert.ThrowsAsync<ImageLoadException>(() => Loader().LoadAsync(sneaky));
    }

    [Fact]
    public async Task A_folder_whose_name_merely_starts_with_the_allowed_one_is_not_inside_it()
    {
        var sibling = _root + "-evil";
        Directory.CreateDirectory(sibling);
        try
        {
            File.WriteAllBytes(Path.Combine(sibling, "x.png"), Images.Png);

            await Assert.ThrowsAsync<ImageLoadException>(() => Loader().LoadAsync(Path.Combine(sibling, "x.png")));
        }
        finally
        {
            Directory.Delete(sibling, true);
        }
    }

    [Fact]
    public async Task A_link_inside_the_allowed_folder_cannot_lead_outside_it()
    {
        var target = Path.Combine(_outside, "real.png");
        File.WriteAllBytes(target, Images.Png);
        var link = Path.Combine(_root, "link.png");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return; // Creating links needs a privilege this machine does not grant.
        }

        await Assert.ThrowsAsync<ImageLoadException>(() => Loader().LoadAsync(link));
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_refused_even_with_an_image_name()
    {
        var path = Path.Combine(_root, "fake.png");
        File.WriteAllText(path, "just text");

        var ex = await Assert.ThrowsAsync<ImageLoadException>(() => Loader().LoadAsync(path));

        Assert.Contains("not a PNG", ex.Message);
    }

    [Fact]
    public async Task An_svg_is_refused_because_it_can_carry_script()
    {
        var path = Path.Combine(_root, "x.svg");
        File.WriteAllBytes(path, Images.Svg);

        await Assert.ThrowsAsync<ImageLoadException>(() => Loader().LoadAsync(path));
    }

    [Fact]
    public async Task An_image_over_the_size_limit_is_refused_before_it_is_read()
    {
        var path = Path.Combine(_root, "big.png");
        File.WriteAllBytes(path, Images.Png.Concat(new byte[100]).ToArray());

        var ex = await Assert.ThrowsAsync<ImageLoadException>(() => Loader(o => o.MaxImageBytes = 50).LoadAsync(path));

        Assert.Contains("limit", ex.Message);
    }

    [Fact]
    public async Task A_missing_file_says_so()
    {
        var ex = await Assert.ThrowsAsync<ImageLoadException>(() => Loader().LoadAsync(Path.Combine(_root, "nope.png")));

        Assert.Contains("No image file", ex.Message);
    }

    // ---- addresses ----

    private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    [Fact]
    public async Task An_https_image_from_an_allowed_host_is_fetched()
    {
        var http = new StubHttp(_ => Ok(Images.Png));

        var image = await Loader(o => o.AllowedUrlHosts = ["img.example.com"], http).LoadAsync("https://img.example.com/a.png");

        Assert.Equal("image/png", image.MediaType);
        Assert.Single(http.Requested);
    }

    [Fact]
    public async Task With_no_allowed_hosts_nothing_is_fetched()
    {
        var http = new StubHttp(_ => Ok(Images.Png));

        await Assert.ThrowsAsync<ImageLoadException>(() => Loader(handler: http).LoadAsync("https://img.example.com/a.png"));

        Assert.Empty(http.Requested);
    }

    [Theory]
    [InlineData("https://evil.com/a.png")]
    [InlineData("https://img.example.com.evil.com/a.png")]
    [InlineData("http://img.example.com/a.png")]
    [InlineData("https://user:pass@img.example.com/a.png")]
    public async Task An_address_off_the_list_or_not_https_or_with_a_password_is_refused_before_any_request(string address)
    {
        var http = new StubHttp(_ => Ok(Images.Png));

        await Assert.ThrowsAsync<ImageLoadException>(() =>
            Loader(o => o.AllowedUrlHosts = ["img.example.com"], http).LoadAsync(address));

        Assert.Empty(http.Requested);
    }

    [Fact]
    public async Task A_redirect_is_reported_and_not_followed()
    {
        var http = new StubHttp(_ => new HttpResponseMessage(HttpStatusCode.Redirect));

        var ex = await Assert.ThrowsAsync<ImageLoadException>(() =>
            Loader(o => o.AllowedUrlHosts = ["img.example.com"], http).LoadAsync("https://img.example.com/a.png"));

        Assert.Contains("302", ex.Message);
    }

    [Fact]
    public async Task A_declared_size_over_the_limit_is_refused_and_so_is_a_stream_that_lies()
    {
        var declared = new StubHttp(_ => Ok(new byte[200]));
        var honest = Loader(o => { o.AllowedUrlHosts = ["img.example.com"]; o.MaxImageBytes = 100; }, declared);
        await Assert.ThrowsAsync<ImageLoadException>(() => honest.LoadAsync("https://img.example.com/a.png"));

        var chunked = new StubHttp(_ =>
        {
            var content = new StreamContent(new MemoryStream(new byte[200]));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        var lying = Loader(o => { o.AllowedUrlHosts = ["img.example.com"]; o.MaxImageBytes = 100; }, chunked);
        await Assert.ThrowsAsync<ImageLoadException>(() => lying.LoadAsync("https://img.example.com/a.png"));
    }

    [Theory]
    [InlineData("*.cdn.example.com", "a.cdn.example.com", true)]
    [InlineData("*.cdn.example.com", "cdn.example.com", false)]
    [InlineData("img.example.com", "IMG.EXAMPLE.COM", true)]
    public void Host_patterns_match_exactly_or_by_subdomain(string pattern, string host, bool allowed) =>
        Assert.Equal(allowed, ImageLoader.HostAllowed([pattern], host));

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.1.1", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fc00::1", true)]
    [InlineData("::ffff:10.0.0.1", true)]
    [InlineData("93.184.216.34", false)]
    [InlineData("2606:2800:220:1:248:1893:25c8:1946", false)]
    public void Internal_addresses_are_recognised(string address, bool expected) =>
        Assert.Equal(expected, ImageLoader.IsInternal(IPAddress.Parse(address)));

    [Fact]
    public async Task A_name_that_resolves_to_loopback_is_refused_at_connect_time()
    {
        // The real handler, not a stub: localhost is allowed by name but resolves to loopback.
        using var loader = new ImageLoader(new VisionOptions { AllowedUrlHosts = ["localhost"], FetchTimeoutSeconds = 5 });

        var ex = await Assert.ThrowsAsync<ImageLoadException>(() => loader.LoadAsync("https://localhost:9/a.png"));

        Assert.Contains("internal", ex.Message);
    }
}

public class VisionToolTests
{
    private static async Task<JsonElement> CallAsync(IReadOnlyList<AITool> tools, string name, Dictionary<string, object?> args)
    {
        var tool = tools.OfType<AIFunction>().Single(t => t.Name == name);
        var result = await tool.InvokeAsync(new AIFunctionArguments(args));
        return JsonDocument.Parse(result is JsonElement e ? e.GetString()! : result!.ToString()!).RootElement;
    }

    private sealed class Rig : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"vtool-{Guid.NewGuid():N}");
        public ScriptedChatClient Client { get; }
        public IReadOnlyList<AITool> Tools { get; }

        public Rig(string reply, bool withStructured)
        {
            Directory.CreateDirectory(Root);
            File.WriteAllBytes(Path.Combine(Root, "a.png"), Images.Png);
            File.WriteAllBytes(Path.Combine(Root, "b.png"), Images.Png);
            Client = new ScriptedChatClient(reply);

            var options = new VisionOptions { AllowedPaths = [Root], MaxImagesPerCall = 2 };
            var loader = new ImageLoader(options);

            IStructuredResponder? responder = null;
            ISchemaCatalog? catalog = null;
            if (withStructured)
            {
                var so = new StructuredOptions { SchemaDirectory = Path.Combine(Root, "none") };
                var cat = new SchemaCatalog(so, NullLogger<SchemaCatalog>.Instance);
                cat.Add("invoice", JsonDocument.Parse("""{"type":"object","properties":{"total":{"type":"number"}},"required":["total"]}""").RootElement);
                catalog = cat;
                responder = new StructuredResponder(Client, so, NullLogger<StructuredResponder>.Instance);
            }

            Tools = VisionToolProvider.CreateTools(Client, loader, options, responder, catalog);
        }

        public string Path_(string name) => Path.Combine(Root, name);

        public void Dispose() => Directory.Delete(Root, true);
    }

    [Fact]
    public async Task Describing_an_image_sends_it_to_the_model_with_the_question()
    {
        using var rig = new Rig("A red square.", false);

        var result = await CallAsync(rig.Tools, "describe_image",
            new() { ["sources"] = new[] { rig.Path_("a.png") }, ["question"] = "What colour?" });

        Assert.Equal("A red square.", result.GetProperty("description").GetString());
        Assert.True(result.GetProperty("untrustedContent").GetBoolean());
        var sent = rig.Client.Calls[0][0];
        Assert.Contains(sent.Contents, c => c is DataContent d && d.MediaType == "image/png");
        Assert.Contains(sent.Contents, c => c is TextContent t && t.Text == "What colour?");
    }

    [Fact]
    public async Task Several_images_go_in_one_call_up_to_the_limit()
    {
        using var rig = new Rig("Two squares.", false);

        await CallAsync(rig.Tools, "describe_image", new() { ["sources"] = new[] { rig.Path_("a.png"), rig.Path_("b.png") } });
        var tooMany = await CallAsync(rig.Tools, "describe_image",
            new() { ["sources"] = new[] { rig.Path_("a.png"), rig.Path_("b.png"), rig.Path_("a.png") } });

        Assert.Equal(2, rig.Client.Calls[0][0].Contents.OfType<DataContent>().Count());
        Assert.Contains("limit", tooMany.GetProperty("error").GetString());
        Assert.Single(rig.Client.Calls);
    }

    [Fact]
    public async Task A_refused_image_is_a_clear_error_and_the_model_is_not_called()
    {
        using var rig = new Rig("x", false);

        var result = await CallAsync(rig.Tools, "describe_image", new() { ["sources"] = new[] { Path.Combine(Path.GetTempPath(), "other.png") } });

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Empty(rig.Client.Calls);
    }

    [Fact]
    public async Task No_sources_is_an_error()
    {
        using var rig = new Rig("x", false);

        var result = await CallAsync(rig.Tools, "describe_image", new() { ["sources"] = Array.Empty<string>() });

        Assert.Contains("at least one", result.GetProperty("error").GetString());
    }

    [Fact]
    public void The_extraction_tool_exists_only_when_structured_output_is_available()
    {
        using var without = new Rig("x", false);
        using var with = new Rig("x", true);

        Assert.DoesNotContain(without.Tools, t => t.Name == "extract_from_image");
        Assert.Contains(with.Tools, t => t.Name == "extract_from_image");
    }

    [Fact]
    public async Task Extracting_from_an_image_returns_a_checked_value()
    {
        using var rig = new Rig("""{"total":42.5}""", true);

        var result = await CallAsync(rig.Tools, "extract_from_image",
            new() { ["sources"] = new[] { rig.Path_("a.png") }, ["instruction"] = "Read the total.", ["schemaName"] = "invoice" });

        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal(42.5, result.GetProperty("value").GetProperty("total").GetDouble());
        Assert.Contains(rig.Client.Calls[0][1].Contents, c => c is DataContent);
    }

    [Fact]
    public async Task A_wrong_answer_from_the_model_is_retried_before_the_tool_reports_it()
    {
        using var rig = new Rig("""{"total":"lots"}""", true);

        var result = await CallAsync(rig.Tools, "extract_from_image",
            new() { ["sources"] = new[] { rig.Path_("a.png") }, ["instruction"] = "Read the total.", ["schemaName"] = "invoice" });

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal(3, result.GetProperty("attempts").GetInt32());
    }
}
