using ChatTerror.Server.Relay;

namespace ChatTerror.Server.Data;

public sealed class ExpiryService(RelayStore store, ConnectionRegistry registry, ILogger<ExpiryService> log) : BackgroundService
{
    public void Sweep()
    {
        foreach (var (deviceId, installId) in store.DeleteExpired())
            registry.Revoke(deviceId, installId, notifyPlugin: true);

        store.DeleteExpiredTells();

        var deleted = store.DeleteStaleInstalls(installId => registry.Plugin(installId) != null);
        if (deleted > 0)
            log.LogInformation("Deleted {Count} unused installs", deleted);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                Sweep();
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Expiry sweep failed");
            }
        }
    }
}
