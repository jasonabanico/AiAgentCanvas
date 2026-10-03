using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Connections;

/// <summary>
/// Refreshes OAuth tokens shortly before they expire, so an unattended run at 3 a.m. does
/// not meet an expired token and does not pay for a refresh. It is also how a revoked
/// authorization is noticed overnight and reported, instead of at the next failed call.
/// </summary>
public sealed class ConnectionRefreshService : BackgroundService
{
    private readonly ConnectionStore _store;
    private readonly ICredentialProvider _credentials;
    private readonly ConnectionsOptions _options;
    private readonly ILogger<ConnectionRefreshService> _logger;
    private readonly TimeProvider _time;

    public ConnectionRefreshService(
        ConnectionStore store,
        ICredentialProvider credentials,
        ConnectionsOptions options,
        ILogger<ConnectionRefreshService> logger,
        TimeProvider? time = null)
    {
        _store = store;
        _credentials = credentials;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _options.SweepIntervalSeconds)));

        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection refresh sweep failed");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    internal async Task SweepAsync(CancellationToken ct)
    {
        var horizon = _time.GetUtcNow().AddMinutes(Math.Max(1, _options.SweepWindowMinutes));

        var due = _store.List().Where(c =>
            c.Auth == AuthKind.OAuth2
            && c.Status == ConnectionStatus.Connected
            && c.ExpiresAt is { } expires
            && expires <= horizon);

        foreach (var connection in due)
        {
            try
            {
                await _credentials.RefreshAsync(connection.Id, ct);
                _logger.LogDebug("Refreshed connection {ConnectionId} ({Connector})", connection.Id, connection.ConnectorId);
            }
            catch (CredentialException ex)
            {
                // A refused authorization is already marked and announced by the provider.
                _logger.LogWarning("Could not refresh connection {ConnectionId}: {Failure}", connection.Id, ex.Failure);
            }
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
