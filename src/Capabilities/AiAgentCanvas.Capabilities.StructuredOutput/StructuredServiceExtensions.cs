using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.StructuredOutput;

public static class StructuredServiceExtensions
{
    public static IServiceCollection AddAiAgentCanvasStructuredOutput(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var options = new StructuredOptions();
        configuration?.GetSection(StructuredOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        services.AddSingleton<SchemaCatalog>();
        services.AddSingleton<ISchemaCatalog>(sp => sp.GetRequiredService<SchemaCatalog>());

        // The answer goes through the same pipeline as an agent's, so it is counted, priced
        // and limited like any other model call.
        services.AddSingleton<IStructuredResponder>(sp => new StructuredResponder(
            sp.GetRequiredKeyedService<IChatClient>(AgentClientKeys.Pipeline),
            sp.GetRequiredService<StructuredOptions>(),
            sp.GetRequiredService<ILogger<StructuredResponder>>()));

        services.AddSingleton<IReadOnlyList<AITool>>(sp => StructuredToolProvider.CreateTools(
            sp.GetRequiredService<IStructuredResponder>(),
            sp.GetRequiredService<ISchemaCatalog>(),
            sp.GetRequiredService<StructuredOptions>()));

        return services;
    }
}
