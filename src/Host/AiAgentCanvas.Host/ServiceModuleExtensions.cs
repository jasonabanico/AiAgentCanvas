using System.Reflection;
using AiAgentCanvas.Abstractions;

namespace AiAgentCanvas.Host;

public static class ServiceModuleExtensions
{
    /// <summary>
    /// Loads every plugin under <paramref name="pluginsDirectory"/> (one subfolder
    /// per plugin, holding that plugin's build output) into its own
    /// <see cref="PluginLoadContext"/>, discovers <see cref="IServiceModule"/>
    /// implementations in each one via reflection, and calls
    /// <see cref="IServiceModule.ConfigureServices"/> for each module whose config
    /// section explicitly sets <c>Enabled</c> to <c>true</c>. Modules are opt-in: a
    /// module with no config section, or with <c>Enabled</c> absent, stays disabled.
    /// </summary>
    /// <remarks>
    /// The Host project never references a plugin assembly directly -- plugins are
    /// discovered purely by folder layout and the shared <see cref="IServiceModule"/>
    /// contract in Abstractions. Dropping a new plugin folder in place, or removing
    /// one, changes what the host loads with no code or project-reference change.
    /// </remarks>
    public static IServiceCollection AddServiceModules(
        this IServiceCollection services,
        IConfiguration configuration,
        string pluginsDirectory = "plugins")
    {
        var root = Path.Combine(AppContext.BaseDirectory, pluginsDirectory);
        if (!Directory.Exists(root)) return services;

        foreach (var pluginDir in Directory.GetDirectories(root))
        {
            var pluginName = Path.GetFileName(pluginDir);
            var assemblyPath = Path.Combine(pluginDir, pluginName + ".dll");
            if (!File.Exists(assemblyPath)) continue;

            var loadContext = new PluginLoadContext(assemblyPath);
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);

            var moduleTypes = GetLoadableTypes(assembly)
                .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IServiceModule).IsAssignableFrom(t));

            foreach (var type in moduleTypes)
            {
                if (Activator.CreateInstance(type) is not IServiceModule module) continue;

                var section = configuration.GetSection(module.SectionName);
                var enabled = section.GetValue("Enabled", false);

                if (!enabled) continue;

                module.ConfigureServices(services, configuration);
            }
        }

        return services;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.OfType<Type>(); }
    }
}
