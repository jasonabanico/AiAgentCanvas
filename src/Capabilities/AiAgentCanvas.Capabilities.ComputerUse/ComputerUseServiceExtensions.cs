using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.ComputerUse;

public static class ComputerUseServiceExtensions
{
    public static IServiceCollection AddAiAgentCanvasComputerUse(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
            new BrowserSession(sp.GetRequiredService<ILogger<BrowserSession>>()));

        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            ComputerUseToolProvider.CreateTools(sp.GetRequiredService<BrowserSession>()));

        return services;
    }
}
