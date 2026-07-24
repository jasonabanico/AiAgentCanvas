using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace DataConnection.MarketData;

public sealed class MarketDataModule : IServiceModule
{
    public string SectionName => "DataConnections:MarketData";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
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

        services.AddSingleton(new ToolStateMapping("stock_quote", ToolStateBehavior.Snapshot));
        services.AddSingleton(new ToolStateMapping("stock_history", ToolStateBehavior.Snapshot));
        services.AddSingleton(new ToolStateMapping("edgar_company_facts", ToolStateBehavior.Snapshot));
    }
}
