using AiAgentCanvas.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.Connections;

public static class ConnectionsServiceExtensions
{
    public static IServiceCollection AddAiAgentCanvasConnections(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new ConnectionsOptions();
        configuration.GetSection(ConnectionsOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        var keyRing = Path.IsPathRooted(options.KeyRingPath)
            ? options.KeyRingPath
            : Path.Combine(Directory.GetCurrentDirectory(), options.KeyRingPath);

        var dataProtection = services.AddDataProtection()
            .SetApplicationName("AiAgentCanvas")
            .PersistKeysToFileSystem(new DirectoryInfo(keyRing));

        if (options.ProtectKeysWithDpapi && OperatingSystem.IsWindows())
            dataProtection.ProtectKeysWithDpapi();

        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        services.AddSingleton(sp =>
        {
            var path = Path.IsPathRooted(options.DatabasePath)
                ? options.DatabasePath
                : Path.Combine(Directory.GetCurrentDirectory(), options.DatabasePath);
            return new ConnectionStore(path, sp.GetRequiredService<ISecretProtector>());
        });

        services.AddHttpClient(OAuthService.HttpClientName, client =>
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.HttpTimeoutSeconds)));

        services.AddSingleton<OAuthProviderRegistry>();
        services.AddSingleton<OAuthService>();
        services.AddSingleton<CredentialProvider>();
        services.AddSingleton<ICredentialProvider>(sp => sp.GetRequiredService<CredentialProvider>());

        services.AddHostedService<ConnectionRefreshService>();

        return services;
    }
}
