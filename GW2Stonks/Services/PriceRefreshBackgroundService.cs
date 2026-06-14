using GW2Stonks.Gw2Api;
using Microsoft.Extensions.Options;

namespace GW2Stonks.Services;

/// <summary>Periodically refreshes trading-post prices in the background.</summary>
public sealed class PriceRefreshBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly Gw2Options _options;
    private readonly ILogger<PriceRefreshBackgroundService> _log;

    public PriceRefreshBackgroundService(
        IServiceScopeFactory scopes,
        IOptions<Gw2Options> options,
        ILogger<PriceRefreshBackgroundService> log)
    {
        _scopes = scopes;
        _options = options.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.PriceRefreshMinutes));
        using var timer = new PeriodicTimer(interval);
        _log.LogInformation("Price refresh service started; interval {Minutes} min", interval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);

                using var scope = _scopes.CreateScope();
                var sync = scope.ServiceProvider.GetRequiredService<Gw2SyncService>();

                var status = await sync.GetStatusAsync(stoppingToken);
                if (status.ItemCount == 0)
                {
                    _log.LogInformation("Skipping price refresh: catalog is empty");
                    continue;
                }

                var count = await sync.TryRefreshPricesAsync(stoppingToken);
                if (count is null)
                    _log.LogInformation("Skipping price refresh: another sync is in progress");
                else
                    _log.LogInformation("Background price refresh updated {Count} prices", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Background price refresh failed");
            }
        }
    }
}
