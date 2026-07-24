using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.Capabilities.SystemTools;

public static class SystemToolsServiceExtensions
{
    public static IServiceCollection AddAiAgentCanvasSystemTools(
        this IServiceCollection services,
        Action<SystemToolOptions>? configure = null)
    {
        var options = new SystemToolOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddSingleton<SystemToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<SystemToolProvider>().GetTools());
        return services;
    }
}
