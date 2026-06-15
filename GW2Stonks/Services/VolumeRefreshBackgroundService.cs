using GW2Stonks.Datawars2;
using Microsoft.Extensions.Options;

namespace GW2Stonks.Services;

/// <summary>Periodically refreshes cached trading-post volume data from datawars2.ie.</summary>
public sealed class VolumeRefreshBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly Datawars2Options _options;
    private readonly ILogger<VolumeRefreshBackgroundService> _log;

    public VolumeRefreshBackgroundService(
        IServiceScopeFactory scopes,
        IOptions<Datawars2Options> options,
        ILogger<VolumeRefreshBackgroundService> log)
    {
        _scopes = scopes;
        _options = options.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Max(1, _options.RefreshHours));
        using var timer = new PeriodicTimer(interval);
        _log.LogInformation("Volume refresh service started; interval {Hours}h", interval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);

                using var scope = _scopes.CreateScope();
                var volume = scope.ServiceProvider.GetRequiredService<VolumeService>();
                var count = await volume.TryRefreshVolumesAsync(stoppingToken);
                if (count is null)
                    _log.LogInformation("Skipping volume refresh: another refresh is in progress");
                else
                    _log.LogInformation("Background volume refresh updated {Count} items", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Background volume refresh failed");
            }
        }
    }
}
