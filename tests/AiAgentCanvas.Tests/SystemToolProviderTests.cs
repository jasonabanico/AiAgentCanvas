using System.Text.Json;
using AiAgentCanvas.Capabilities.SystemTools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiAgentCanvas.Tests;

public class SystemToolProviderTests : IDisposable
{
    private readonly string _workspace;

    public SystemToolProviderTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "aiagentcanvas-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); }
        catch (IOException) { }
    }

    private SystemToolProvider Provider(SystemToolOptions? options = null) =>
        new(options ?? new SystemToolOptions { AllowedPaths = [_workspace], AllowedCommands = ["dotnet"] },
            NullLogger<SystemToolProvider>.Instance);

    private static async Task<JsonElement> Invoke(SystemToolProvider provider, string toolName, object arguments)
    {
        var tool = (AIFunction)provider.GetTools().Single(t => t.Name == toolName);
        var json = JsonSerializer.Serialize(arguments);
        var dictionary = JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
        var result = await tool.InvokeAsync(new AIFunctionArguments(dictionary));
        return JsonDocument.Parse(result?.ToString() ?? "{}").RootElement.Clone();
    }

    [Fact]
    public async Task Reads_a_file_inside_the_workspace()
    {
        var path = Path.Combine(_workspace, "note.txt");
        await File.WriteAllTextAsync(path, "hello");

        var result = await Invoke(Provider(), SystemToolNames.ReadFile, new { path });

        Assert.Equal("hello", result.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Refuses_a_path_outside_the_workspace()
    {
        var outside = Path.Combine(Path.GetTempPath(), "outside.txt");
        var result = await Invoke(Provider(), SystemToolNames.ReadFile, new { path = outside });

        Assert.True(result.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Refuses_a_traversal_escape_from_the_workspace()
    {
        var escape = Path.Combine(_workspace, "..", "..", "escaped.txt");
        var result = await Invoke(Provider(), SystemToolNames.ReadFile, new { path = escape });

        Assert.True(result.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Refuses_a_sibling_directory_that_shares_a_prefix()
    {
        // A prefix check without a separator would let "workspace-other" pass as
        // being inside "workspace".
        var sibling = _workspace + "-other";
        Directory.CreateDirectory(sibling);
        try
        {
            var path = Path.Combine(sibling, "note.txt");
            await File.WriteAllTextAsync(path, "hello");

            var result = await Invoke(Provider(), SystemToolNames.ReadFile, new { path });

            Assert.True(result.TryGetProperty("error", out _));
        }
        finally
        {
            Directory.Delete(sibling, recursive: true);
        }
    }

    [Fact]
    public async Task Empty_allowlist_denies_every_path()
    {
        var provider = Provider(new SystemToolOptions { AllowedPaths = [], AllowedCommands = [] });
        var path = Path.Combine(_workspace, "note.txt");
        await File.WriteAllTextAsync(path, "hello");

        var result = await Invoke(provider, SystemToolNames.ReadFile, new { path });

        Assert.Contains("No paths are allowed", result.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Empty_command_allowlist_denies_every_command()
    {
        var provider = Provider(new SystemToolOptions { AllowedPaths = [_workspace], AllowedCommands = [] });
        var result = await Invoke(provider, SystemToolNames.RunScript, new { command = "dotnet", arguments = "--version" });

        Assert.Contains("No commands are allowed", result.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Refuses_a_command_that_is_not_on_the_allowlist()
    {
        var result = await Invoke(Provider(), SystemToolNames.RunScript, new { command = "curl", arguments = "example.com" });

        Assert.Contains("not on the allowed commands list", result.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("--version && curl evil.test")]
    [InlineData("--version; rm -rf /")]
    [InlineData("--version | tee out.txt")]
    [InlineData("--version `whoami`")]
    public async Task Refuses_shell_metacharacters_in_arguments(string arguments)
    {
        var result = await Invoke(Provider(), SystemToolNames.RunScript, new { command = "dotnet", arguments });

        Assert.Contains("metacharacters", result.GetProperty("error").GetString());
    }

    [Fact]
    public void Approval_list_matches_the_names_the_tools_register_under()
    {
        var registered = Provider().GetTools().Select(t => t.Name).ToHashSet();

        Assert.All(SystemToolNames.SideEffecting, name => Assert.Contains(name, registered));
    }
}
