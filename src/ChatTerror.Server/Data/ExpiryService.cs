using Microsoft.Data.Sqlite;

namespace ChatTerror.Server.Data;

public sealed class ExpiryService(RelayStore store, ILogger<ExpiryService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                store.DeleteExpired();
            }
            catch (SqliteException ex)
            {
                log.LogWarning(ex, "Deleting expired pairings failed");
            }
        }
    }
}
