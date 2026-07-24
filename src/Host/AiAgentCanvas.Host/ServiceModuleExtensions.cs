using System.Reflection;
using AiAgentCanvas.Abstractions;

namespace AiAgentCanvas.Host;

public static class ServiceModuleExtensions
{
    /// <summary>
    /// Discovers all <see cref="IServiceModule"/> implementations in referenced assemblies
    /// and calls <see cref="IServiceModule.ConfigureServices"/> for each one whose config
    /// section does not set <c>Enabled</c> to <c>false</c>.
    /// </summary>
    public static IServiceCollection AddServiceModules(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var moduleTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch (ReflectionTypeLoadException e) { return e.Types.OfType<Type>(); }
            })
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IServiceModule).IsAssignableFrom(t));

        foreach (var type in moduleTypes)
        {
            if (Activator.CreateInstance(type) is not IServiceModule module) continue;

            var section = configuration.GetSection(module.SectionName);
            var enabled = section.GetValue("Enabled", true);

            if (!enabled) continue;

            module.ConfigureServices(services, configuration);
        }

        return services;
    }
}
