using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace AiAgentCanvas.Connectors;

public static class ConnectorServiceExtensions
{
    /// <summary>
    /// Registers the connector host. It needs the connections capability for credentials
    /// and the dynamic tool registry from the orchestration project. Add each connector
    /// type with <see cref="AddConnector"/>.
    /// </summary>
    public static IServiceCollection AddAiAgentCanvasConnectors(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new ConnectorOptions();
        configuration.GetSection(ConnectorOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        services.AddSingleton<ConnectorRegistry>();
        services.TryAddSingleton<ConnectorHost>();
        services.AddHostedService(sp => sp.GetRequiredService<ConnectorHost>());

        return services;
    }

    /// <summary>
    /// Registers a connector type and its HTTP client: no redirects, so a response cannot
    /// steer a credential to another host, and standard resilience that retries only
    /// requests that are safe to repeat. A send is never retried automatically.
    /// </summary>
    public static IServiceCollection AddConnector(this IServiceCollection services, IConnectorDefinition definition)
    {
        services.AddSingleton(definition);

        services.AddHttpClient(ConnectorHttp.ClientName(definition.Descriptor.Id))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            })
            .AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());

        return services;
    }
}
