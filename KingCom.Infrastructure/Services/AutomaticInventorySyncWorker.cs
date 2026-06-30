using KingCom.Domain.Options;
using KingCom.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KingCom.Infrastructure.Services;

public sealed class AutomaticInventorySyncWorker(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<SyncOptions> syncOptions,
    IOptionsMonitor<HaravanOptions> haravanOptions,
    JsonlSyncLogger logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = syncOptions.CurrentValue;
            var interval = TimeSpan.FromMinutes(Math.Clamp(options.AutoIntervalMinutes, 1, 1440));

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (!syncOptions.CurrentValue.AutoEnabled)
            {
                continue;
            }

            if (haravanOptions.CurrentValue.BlockWrites)
            {
                logger.Write("auto_sync_skipped", new { reason = "Haravan:BlockWrites=true" });
                continue;
            }

            await RunAutoSyncAsync(stoppingToken);
        }
    }

    private async Task RunAutoSyncAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.Write("auto_sync_started", new { dryRun = false });
            using var scope = scopeFactory.CreateScope();
            var sync = scope.ServiceProvider.GetRequiredService<InventorySyncService>();
            var result = await sync.SyncAsync(dryRun: false, cancellationToken);
            logger.Write("auto_sync_finished", new
            {
                result.Total,
                result.Success,
                result.Failed,
                result.Message
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Application is shutting down.
        }
        catch (Exception ex)
        {
            logger.Write("auto_sync_failed", new { error = ex.Message });
        }
    }
}
