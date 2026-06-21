using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace MCP.MarketData;

public static class MarketDataServiceExtensions
{
    public static IServiceCollection AddMarketDataTools(this IServiceCollection services)
    {
        services.AddHttpClient("SEC", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AiAgentCanvas/1.0 (contact@example.com)");
            })
            .AddStandardResilienceHandler();

        services.AddHttpClient("Yahoo", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AiAgentCanvas/1.0");
            })
            .AddStandardResilienceHandler();

        services.AddSingleton<MarketDataToolProvider>();
        services.AddSingleton<IReadOnlyList<AITool>>(sp => sp.GetRequiredService<MarketDataToolProvider>().GetTools());
        return services;
    }
}
