using System.Reflection;
using System.Runtime.Loader;

namespace AiAgentCanvas.Host;

/// <summary>
/// Isolated load context for one plugin assembly. Resolves the plugin's own
/// private dependencies from its folder, but defers to whatever is already
/// loaded in the default context for anything the host and the plugin share
/// (the contract assembly, Microsoft.Extensions.*, etc.) -- without that, the
/// plugin's copy of a shared type would be a distinct runtime type from the
/// host's copy, and interface checks like <c>is IServiceModule</c> would fail
/// even though the source code is identical.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginMainAssemblyPath)
        : base(name: Path.GetFileNameWithoutExtension(pluginMainAssemblyPath), isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginMainAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var shared = Default.Assemblies.FirstOrDefault(a => a.GetName().Name == assemblyName.Name);
        if (shared is not null) return shared;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is not null ? LoadUnmanagedDllFromPath(path) : nint.Zero;
    }
}
