using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.SystemTools;

/// <summary>
/// Tool names published by this capability. Governance policy references these, so
/// having one definition keeps an approval list from silently matching nothing.
/// </summary>
public static class SystemToolNames
{
    public const string ReadFile = "system_read_file";
    public const string WriteFile = "system_write_file";
    public const string ListDirectory = "system_list_directory";
    public const string RunScript = "system_run_script";

    /// <summary>Tools that change the host and should not run without a human saying yes.</summary>
    public static readonly string[] SideEffecting = [WriteFile, RunScript];
}

public sealed class SystemToolOptions
{
    /// <summary>
    /// Directories the file tools may touch. Empty denies every path: an agent with
    /// unrestricted filesystem access is not a sandbox, and defaulting to open means
    /// forgetting to configure it is indistinguishable from choosing it.
    /// </summary>
    public List<string> AllowedPaths { get; set; } = [];

    /// <summary>
    /// Executables the agent may run. Empty denies every command, for the same reason.
    /// </summary>
    public List<string> AllowedCommands { get; set; } = [];

    public int MaxFileSizeBytes { get; set; } = 1_048_576;
    public int ScriptTimeoutSeconds { get; set; } = 30;
}

public sealed class SystemToolProvider
{
    // Shell metacharacters. Commands run without a shell, so these cannot chain
    // anything, but rejecting them keeps intent-versus-effect surprises out.
    private static readonly char[] ShellMetacharacters = ['|', '&', ';', '>', '<', '`', '$', '\n', '\r'];

    private readonly SystemToolOptions _options;
    private readonly ILogger<SystemToolProvider> _logger;

    public SystemToolProvider(SystemToolOptions options, ILogger<SystemToolProvider> logger)
    {
        _options = options;
        _logger = logger;

        if (_options.AllowedPaths.Count == 0)
            _logger.LogWarning("SystemTools has no allowed paths configured, so the file tools will refuse every path.");
        if (_options.AllowedCommands.Count == 0)
            _logger.LogWarning("SystemTools has no allowed commands configured, so the script tool will refuse every command.");
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(ReadFile, SystemToolNames.ReadFile,
                "Read the contents of a file inside the agent's allowed directories"),
            AIFunctionFactory.Create(WriteFile, SystemToolNames.WriteFile,
                "Write content to a file inside the agent's allowed directories"),
            AIFunctionFactory.Create(ListDirectory, SystemToolNames.ListDirectory,
                "List files and directories at a path inside the agent's allowed directories"),
            AIFunctionFactory.Create(RunScript, SystemToolNames.RunScript,
                "Run one allowed executable with arguments. No shell, so pipes and redirection are not available"),
        ];
    }

    [Description("Read the contents of a file inside the agent's allowed directories")]
    private string ReadFile(
        [Description("Path to the file, inside an allowed directory")] string path)
    {
        if (!TryResolvePath(path, out var fullPath, out var error))
            return error;

        if (!File.Exists(fullPath))
            return Error($"File not found: {fullPath}");

        var info = new FileInfo(fullPath);
        if (info.Length > _options.MaxFileSizeBytes)
            return Error($"File too large ({info.Length} bytes). Max: {_options.MaxFileSizeBytes}");

        var content = File.ReadAllText(fullPath);
        _logger.LogInformation("Read file {Path} ({Bytes} bytes)", fullPath, info.Length);

        return JsonSerializer.Serialize(new { path = fullPath, size = info.Length, content });
    }

    [Description("Write content to a file inside the agent's allowed directories")]
    private string WriteFile(
        [Description("Path to the file, inside an allowed directory")] string path,
        [Description("Content to write to the file")] string content)
    {
        if (!TryResolvePath(path, out var fullPath, out var error))
            return error;

        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, content);
        _logger.LogInformation("Wrote file {Path} ({Bytes} bytes)", fullPath, content.Length);

        return JsonSerializer.Serialize(new { status = "written", path = fullPath, size = content.Length });
    }

    [Description("List files and directories at a path inside the agent's allowed directories")]
    private string ListDirectory(
        [Description("Path to the directory, inside an allowed directory")] string path)
    {
        if (!TryResolvePath(path, out var fullPath, out var error))
            return error;

        if (!Directory.Exists(fullPath))
            return Error($"Directory not found: {fullPath}");

        var entries = Directory.GetFileSystemEntries(fullPath)
            .Select(e =>
            {
                var isDir = Directory.Exists(e);
                return new
                {
                    name = Path.GetFileName(e),
                    type = isDir ? "directory" : "file",
                    size = isDir ? (long?)null : new FileInfo(e).Length,
                };
            })
            .ToList();

        _logger.LogInformation("Listed directory {Path} ({Count} entries)", fullPath, entries.Count);

        return JsonSerializer.Serialize(new { path = fullPath, count = entries.Count, entries },
            new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Run one allowed executable with arguments. No shell, so pipes and redirection are not available")]
    private async Task<string> RunScript(
        [Description("The executable to run, which must be on the allowed commands list")] string command,
        [Description("Arguments passed to the executable")] string? arguments,
        CancellationToken ct)
    {
        if (_options.AllowedCommands.Count == 0)
            return Error("No commands are allowed. Configure SystemTools:AllowedCommands to enable this tool.");

        var executable = command.Trim();
        if (executable.Length == 0)
            return Error("No command given.");

        if (!_options.AllowedCommands.Any(c => c.Equals(executable, StringComparison.OrdinalIgnoreCase)))
            return Error($"Command '{executable}' is not on the allowed commands list.");

        if (executable.IndexOfAny(ShellMetacharacters) >= 0 || (arguments?.IndexOfAny(ShellMetacharacters) ?? -1) >= 0)
            return Error("Shell metacharacters are not permitted. Pass the executable and its arguments separately.");

        _logger.LogInformation("Executing {Command} {Arguments}", executable, arguments);

        // Invoked directly rather than through cmd.exe or /bin/sh: with no shell in
        // the path, a crafted argument cannot chain a second command.
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments ?? string.Empty,
            WorkingDirectory = _options.AllowedPaths.FirstOrDefault() ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
            return Error("Failed to start process");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.ScriptTimeoutSeconds));

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            _logger.LogInformation("Command completed with exit code {ExitCode}", process.ExitCode);

            return JsonSerializer.Serialize(new
            {
                exitCode = process.ExitCode,
                stdout = Truncate(stdout, 10_000),
                stderr = Truncate(stderr, 2_000),
            });
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return Error($"Command timed out after {_options.ScriptTimeoutSeconds} seconds");
        }
    }

    /// <summary>
    /// Resolves the path and confirms it stays inside an allowed root. Comparison is
    /// on the canonical path with a trailing separator, so <c>/data-other</c> cannot
    /// pass as a prefix match for <c>/data</c>, and <c>..</c> is already collapsed.
    /// </summary>
    private bool TryResolvePath(string path, out string fullPath, out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;

        if (_options.AllowedPaths.Count == 0)
        {
            error = Error("No paths are allowed. Configure SystemTools:AllowedPaths to enable the file tools.");
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            error = Error($"Invalid path '{path}': {ex.Message}");
            return false;
        }

        var candidate = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var allowed in _options.AllowedPaths)
        {
            var root = Path.GetFullPath(allowed)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (candidate.Equals(root, StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        _logger.LogWarning("Denied access to {Path}, which is outside every allowed directory", fullPath);
        error = Error($"Path '{path}' is outside the allowed directories.");
        return false;
    }

    private static string Error(string message) => JsonSerializer.Serialize(new { error = message });

    private static string Truncate(string text, int max) =>
        text.Length > max ? text[..max] + "\n...(truncated)" : text;
}
