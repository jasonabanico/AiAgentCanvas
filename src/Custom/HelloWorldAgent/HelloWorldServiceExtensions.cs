using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HelloWorldAgent;

public static class HelloWorldServiceExtensions
{
    public static IServiceCollection AddHelloWorldAgent(this IServiceCollection services)
    {
        services.AddSingleton<HelloWorldToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            sp.GetRequiredService<HelloWorldToolProvider>().GetTools());
        return services;
    }
}
