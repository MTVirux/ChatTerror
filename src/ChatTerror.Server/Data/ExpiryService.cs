using ChatTerror.Server.Relay;

namespace ChatTerror.Server.Data;

public sealed class ExpiryService(RelayStore store, ConnectionRegistry registry, ILogger<ExpiryService> log) : BackgroundService
{
    public void Sweep()
    {
        foreach (var (deviceId, installId) in store.DeleteExpired())
            registry.Revoke(deviceId, installId, notifyPlugin: true);

        store.DeleteExpiredTells();

        var devices = store.DeleteInactiveDevices(deviceId => registry.Device(deviceId) != null);
        foreach (var (deviceId, installId) in devices)
            registry.Revoke(deviceId, installId, notifyPlugin: true);
        if (devices.Count > 0)
            log.LogInformation("Deleted {Count} inactive devices", devices.Count);

        var installs = store.DeleteStaleInstalls(installId => registry.Plugin(installId) != null);
        foreach (var (installId, deviceIds) in installs)
        {
            foreach (var deviceId in deviceIds)
                registry.Revoke(deviceId, installId, notifyPlugin: false);
        }
        if (installs.Count > 0)
            log.LogInformation("Deleted {Count} unused installs", installs.Count);
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
