using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.SystemTools;

public sealed class SystemToolOptions
{
    public List<string> AllowedPaths { get; set; } = [];
    public List<string> AllowedCommands { get; set; } = [];
    public int MaxFileSizeBytes { get; set; } = 1_048_576; // 1MB
    public int ScriptTimeoutSeconds { get; set; } = 30;
}

public sealed class SystemToolProvider
{
    private readonly SystemToolOptions _options;
    private readonly ILogger<SystemToolProvider> _logger;

    public SystemToolProvider(SystemToolOptions options, ILogger<SystemToolProvider> logger)
    {
        _options = options;
        _logger = logger;
    }

    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(ReadFile, "system_read_file",
                "Read the contents of a file from the local filesystem"),
            AIFunctionFactory.Create(WriteFile, "system_write_file",
                "Write content to a file on the local filesystem"),
            AIFunctionFactory.Create(ListDirectory, "system_list_directory",
                "List files and directories at a given path"),
            AIFunctionFactory.Create(RunScript, "system_run_script",
                "Execute a shell command or script on the host machine"),
        ];
    }

    [Description("Read the contents of a file from the local filesystem")]
    private string ReadFile(
        [Description("Absolute or relative path to the file")] string path)
    {
        if (!IsPathAllowed(path))
            return JsonSerializer.Serialize(new { error = $"Path '{path}' is outside the allowed directories" });

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            return JsonSerializer.Serialize(new { error = $"File not found: {fullPath}" });

        var info = new FileInfo(fullPath);
        if (info.Length > _options.MaxFileSizeBytes)
            return JsonSerializer.Serialize(new { error = $"File too large ({info.Length} bytes). Max: {_options.MaxFileSizeBytes}" });

        var content = File.ReadAllText(fullPath);
        _logger.LogInformation("Read file {Path} ({Bytes} bytes)", fullPath, info.Length);

        return JsonSerializer.Serialize(new { path = fullPath, size = info.Length, content });
    }

    [Description("Write content to a file on the local filesystem")]
    private string WriteFile(
        [Description("Absolute or relative path to the file")] string path,
        [Description("Content to write to the file")] string content)
    {
        if (!IsPathAllowed(path))
            return JsonSerializer.Serialize(new { error = $"Path '{path}' is outside the allowed directories" });

        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, content);
        _logger.LogInformation("Wrote file {Path} ({Bytes} bytes)", fullPath, content.Length);

        return JsonSerializer.Serialize(new { status = "written", path = fullPath, size = content.Length });
    }

    [Description("List files and directories at a given path")]
    private string ListDirectory(
        [Description("Absolute or relative path to the directory")] string path)
    {
        if (!IsPathAllowed(path))
            return JsonSerializer.Serialize(new { error = $"Path '{path}' is outside the allowed directories" });

        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
            return JsonSerializer.Serialize(new { error = $"Directory not found: {fullPath}" });

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

    [Description("Execute a shell command or script on the host machine")]
    private async Task<string> RunScript(
        [Description("The command to execute")] string command,
        CancellationToken ct)
    {
        if (_options.AllowedCommands.Count > 0)
        {
            var exe = command.Split(' ', 2)[0];
            if (!_options.AllowedCommands.Any(c => c.Equals(exe, StringComparison.OrdinalIgnoreCase)))
                return JsonSerializer.Serialize(new { error = $"Command '{exe}' is not in the allowed commands list" });
        }

        _logger.LogInformation("Executing command: {Command}", command);

        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : "/bin/sh",
            Arguments = isWindows ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
            return JsonSerializer.Serialize(new { error = "Failed to start process" });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.ScriptTimeoutSeconds));

        try
        {
            var stdout = await process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = await process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            _logger.LogInformation("Command completed with exit code {ExitCode}", process.ExitCode);

            return JsonSerializer.Serialize(new
            {
                exitCode = process.ExitCode,
                stdout = stdout.Length > 10_000 ? stdout[..10_000] + "\n...(truncated)" : stdout,
                stderr = stderr.Length > 2_000 ? stderr[..2_000] + "\n...(truncated)" : stderr,
            });
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return JsonSerializer.Serialize(new { error = $"Command timed out after {_options.ScriptTimeoutSeconds} seconds" });
        }
    }

    private bool IsPathAllowed(string path)
    {
        if (_options.AllowedPaths.Count == 0)
            return true;

        var fullPath = Path.GetFullPath(path);
        return _options.AllowedPaths.Any(allowed =>
            fullPath.StartsWith(Path.GetFullPath(allowed), StringComparison.OrdinalIgnoreCase));
    }
}
