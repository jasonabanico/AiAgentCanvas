using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.EpisodicMemory;

public sealed class MemoryDecayService : BackgroundService
{
    private readonly EpisodicMemoryStore _store;
    private readonly ILogger<MemoryDecayService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(6);

    public MemoryDecayService(EpisodicMemoryStore store, ILogger<MemoryDecayService> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, stoppingToken);
                _store.ApplyDecay();
                _logger.LogDebug("Memory decay cycle completed");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
