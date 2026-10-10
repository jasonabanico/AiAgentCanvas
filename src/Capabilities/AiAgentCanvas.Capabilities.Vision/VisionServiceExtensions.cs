using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.Capabilities.Vision;

public static class VisionServiceExtensions
{
    /// <summary>
    /// Adds image tools. The model behind the pipeline must accept images. If the
    /// StructuredOutput capability is on, <c>extract_from_image</c> is added too.
    /// </summary>
    public static IServiceCollection AddAiAgentCanvasVision(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var options = new VisionOptions();
        configuration?.GetSection(VisionOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        services.AddSingleton(sp => new ImageLoader(sp.GetRequiredService<VisionOptions>()));

        services.AddSingleton<IReadOnlyList<AITool>>(sp => VisionToolProvider.CreateTools(
            sp.GetRequiredKeyedService<IChatClient>(AgentClientKeys.Pipeline),
            sp.GetRequiredService<ImageLoader>(),
            sp.GetRequiredService<VisionOptions>(),
            sp.GetService<IStructuredResponder>(),
            sp.GetService<ISchemaCatalog>()));

        return services;
    }
}
