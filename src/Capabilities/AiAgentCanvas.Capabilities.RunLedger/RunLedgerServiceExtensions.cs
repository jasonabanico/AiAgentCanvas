using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentCanvas.Capabilities.RunLedger;

public static class RunLedgerServiceExtensions
{
    public static IServiceCollection AddAiAgentCanvasRunLedger(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var options = new RunLedgerOptions();
        configuration?.GetSection(RunLedgerOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        services.AddSingleton<IRunLedger>(sp =>
        {
            var o = sp.GetRequiredService<RunLedgerOptions>();
            var path = Path.IsPathRooted(o.DatabasePath)
                ? o.DatabasePath
                : Path.Combine(Directory.GetCurrentDirectory(), o.DatabasePath);
            return new SqliteRunLedger(path, o.MaxTextChars);
        });

        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            RunLedgerToolProvider.CreateTools(sp.GetRequiredService<IRunLedger>()));

        services.AddHostedService<RunLedgerMaintenanceService>();

        // Available to the job runner when the Jobs capability is on. Harmless otherwise.
        services.AddSingleton<IAgentJob, RunFailureReportJob>();

        return services;
    }
}
