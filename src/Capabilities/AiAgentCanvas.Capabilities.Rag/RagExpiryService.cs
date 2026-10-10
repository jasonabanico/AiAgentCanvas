using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.Rag;

/// <summary>Deletes chunks older than <see cref="RagOptions.TimeToLiveDays"/>. Registered only when that is above zero.</summary>
public sealed class RagExpiryService : BackgroundService
{
    private readonly IDocumentIndex _index;
    private readonly RagOptions _options;
    private readonly ILogger<RagExpiryService> _logger;
    private readonly TimeProvider _time;

    public RagExpiryService(IDocumentIndex index, RagOptions options, ILogger<RagExpiryService> logger, TimeProvider? time = null)
    {
        _index = index;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task ExpireOnceAsync(CancellationToken ct)
    {
        if (_options.TimeToLiveDays <= 0)
            return;

        var cutoff = _time.GetUtcNow().AddDays(-_options.TimeToLiveDays);
        var removed = await _index.DeleteIndexedBeforeAsync(cutoff, ct);
        if (removed > 0)
            _logger.LogInformation("Expired {Count} chunks indexed before {Cutoff:u}", removed, cutoff);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireOnceAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Expiring old documents failed. Trying again later");
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }
    }
}
