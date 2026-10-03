using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.Capabilities.Jobs;

public static class JobServiceExtensions
{
    public static IServiceCollection AddAiAgentCanvasJobs(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var options = new JobOptions();
        configuration?.GetSection(JobOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        services.AddSingleton<JobRunner>();
        services.AddSingleton<IJobRunner>(sp => sp.GetRequiredService<JobRunner>());

        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            JobToolProvider.CreateTools(sp.GetRequiredService<IJobRunner>()));

        return services;
    }

    /// <summary>
    /// Registers a job. Any project can contribute jobs this way without referencing the
    /// Jobs capability, because a job is just an <see cref="IAgentJob"/>.
    /// </summary>
    public static IServiceCollection AddAgentJob<TJob>(this IServiceCollection services)
        where TJob : class, IAgentJob
    {
        services.AddSingleton<IAgentJob, TJob>();
        return services;
    }
}
