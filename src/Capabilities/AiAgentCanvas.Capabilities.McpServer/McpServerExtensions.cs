using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace AiAgentCanvas.Capabilities.McpServer;

public static class McpServerExtensions
{
    /// <summary>
    /// Serves a chosen set of this host's tools over MCP, so another agent, an IDE or a
    /// workflow engine can call them. Nothing is exposed until
    /// <c>Agent:McpServer:ExposedTools</c> names it.
    /// </summary>
    public static IServiceCollection AddAiAgentCanvasMcpServer(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var options = new McpServerOptions();
        configuration?.GetSection(McpServerOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        services.AddSingleton(sp => new ExposedToolSet(sp, sp.GetRequiredService<McpServerOptions>()));

        services.AddMcpServer(o =>
        {
            o.ServerInfo = new() { Name = options.ServerName, Version = "1.0.0" };
        }).WithHttpTransport();

        // The tool list needs the finished service provider, and the SDK fills the same
        // collection from its own setup, so this runs after it and adds to what is there.
        services.AddOptions<ModelContextProtocol.Server.McpServerOptions>()
            .PostConfigure<ExposedToolSet>((o, set) =>
            {
                o.ToolCollection ??= [];
                foreach (var tool in set.Tools)
                    o.ToolCollection.Add(McpServerTool.Create(tool));
            });

        return services;
    }

    public static IEndpointConventionBuilder MapAiAgentCanvasMcp(this IEndpointRouteBuilder endpoints, McpServerOptions options) =>
        endpoints.MapMcp(options.Path);
}
