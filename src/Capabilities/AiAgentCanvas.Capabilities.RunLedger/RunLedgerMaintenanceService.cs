using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.RunLedger;

/// <summary>
/// Closes out runs a previous process left in flight, then deletes finished runs
/// past the retention window once a day.
/// </summary>
public sealed class RunLedgerMaintenanceService : BackgroundService
{
    private readonly IRunLedger _ledger;
    private readonly RunLedgerOptions _options;
    private readonly ILogger<RunLedgerMaintenanceService> _logger;

    public RunLedgerMaintenanceService(
        IRunLedger ledger,
        RunLedgerOptions options,
        ILogger<RunLedgerMaintenanceService> logger)
    {
        _ledger = ledger;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var abandoned = _ledger.MarkAbandoned();
        if (abandoned > 0)
            _logger.LogWarning("{Count} run(s) were still in flight when the previous process stopped", abandoned);

        if (_options.RetentionDays <= 0)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                var removed = _ledger.Prune(DateTimeOffset.UtcNow.AddDays(-_options.RetentionDays));
                if (removed > 0)
                    _logger.LogInformation("Pruned {Count} run(s) older than {Days} days", removed, _options.RetentionDays);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Run ledger pruning failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
