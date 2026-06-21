using GW2Stonks.Data;
using GW2Stonks.Data.Entities;
using GW2Stonks.Datawars2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GW2Stonks.Services;

/// <summary>
/// Refreshes cached trading-post liquidity (sold/day, supply/demand) for craftable, tradable items
/// from datawars2.ie and stores it in the local <see cref="ItemVolume"/> table.
/// </summary>
public sealed class VolumeService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly Datawars2Client _api;
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly Datawars2Options _options;
    private readonly ILogger<VolumeService> _log;

    public VolumeService(
        Datawars2Client api,
        IDbContextFactory<AppDbContext> dbf,
        IOptions<Datawars2Options> options,
        ILogger<VolumeService> log)
    {
        _api = api;
        _dbf = dbf;
        _options = options.Value;
        _log = log;
    }

    public async Task<int> RefreshVolumesAsync(IProgress<SyncProgress>? progress = null, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try { return await RefreshCoreAsync(progress, ct); }
        finally { Gate.Release(); }
    }

    /// <summary>Refresh only if no refresh is already running; returns null if skipped.</summary>
    public async Task<int?> TryRefreshVolumesAsync(CancellationToken ct = default)
    {
        if (!await Gate.WaitAsync(0, ct)) return null;
        try { return await RefreshCoreAsync(null, ct); }
        finally { Gate.Release(); }
    }

    /// <summary>
    /// Return sold/day for the given items, fetching + caching volume for any not already in the DB.
    /// Lets the listings page rate raw materials (which sit outside the craftable set the full
    /// refresh covers). Best-effort: if caching fails, the freshly computed figures are still used.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, int>> EnsureSoldPerDayAsync(
        IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var have = await db.ItemVolumes.AsNoTracking().Where(v => ids.Contains(v.ItemId))
            .ToDictionaryAsync(v => v.ItemId, v => v.SoldPerDay, ct);

        var missing = ids.Where(id => !have.ContainsKey(id)).Distinct().ToList();
        if (missing.Count == 0) return have;

        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-Math.Max(1, _options.HistoryDays)));
        var rows = await FetchHistoryAsync(missing, start, null, ct);
        var volumes = rows.GroupBy(r => r.ItemId).Select(g => BuildVolume(g.Key, g)).ToList();

        foreach (var v in volumes) have[v.ItemId] = v.SoldPerDay; // use computed values even if caching fails
        try { if (volumes.Count > 0) await UpsertAsync(db, volumes, null, ct); }
        catch (Exception ex) { _log.LogWarning(ex, "Could not cache on-demand volumes for {N} items", volumes.Count); }
        return have;
    }

    private async Task<int> RefreshCoreAsync(IProgress<SyncProgress>? progress, CancellationToken ct)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);

        // Target the set the profit page cares about: craftable (recipe outputs) and tradable (priced).
        var targetIds = await db.Recipes.AsNoTracking()
            .Select(r => r.OutputItemId)
            .Distinct()
            .Where(id => db.Prices.Any(p => p.ItemId == id))
            .ToListAsync(ct);

        if (targetIds.Count == 0) return 0;

        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-Math.Max(1, _options.HistoryDays)));
        var rows = await FetchHistoryAsync(targetIds, start, progress, ct);

        var volumes = rows.GroupBy(r => r.ItemId).Select(g => BuildVolume(g.Key, g)).ToList();

        await UpsertAsync(db, volumes, progress, ct);
        await UpdateSyncStateAsync(db, "volume", volumes.Count, ct);
        _log.LogInformation("Refreshed volume for {Count} items", volumes.Count);
        return volumes.Count;
    }

    private async Task<List<Datawars2HistoryDto>> FetchHistoryAsync(
        IReadOnlyList<int> ids, DateOnly start, IProgress<SyncProgress>? progress, CancellationToken ct)
    {
        var batches = ids.Chunk(_options.BatchSize).ToList();
        var results = new List<Datawars2HistoryDto>();
        var failures = new List<Exception>();
        int done = 0;
        var gate = new object();
        progress?.Report(new SyncProgress("Fetching volume", 0, ids.Count));

        await Parallel.ForEachAsync(
            batches,
            new ParallelOptions { MaxDegreeOfParallelism = _options.MaxConcurrency, CancellationToken = ct },
            async (batch, _) =>
            {
                try
                {
                    var part = await _api.GetHistoryAsync(batch, start, ct);
                    lock (gate)
                    {
                        results.AddRange(part);
                        done += batch.Length;
                        progress?.Report(new SyncProgress("Fetching volume", done, ids.Count));
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    lock (gate) failures.Add(ex);
                }
            });

        if (failures.Count > 0 && results.Count == 0)
            throw new InvalidOperationException($"Volume fetch failed: {failures[0].Message}", failures[0]);
        if (failures.Count > 0)
            _log.LogWarning("{N} volume batch(es) failed; continuing with partial data", failures.Count);

        return results;
    }

    private async Task UpsertAsync(AppDbContext db, List<ItemVolume> volumes, IProgress<SyncProgress>? progress, CancellationToken ct)
    {
        int saved = 0;
        progress?.Report(new SyncProgress("Saving volume", 0, volumes.Count));
        foreach (var chunk in volumes.Chunk(1000))
        {
            var chunkIds = chunk.Select(v => v.ItemId).ToList();
            var existing = await db.ItemVolumes.AsNoTracking()
                .Where(v => chunkIds.Contains(v.ItemId)).Select(v => v.ItemId).ToHashSetAsync(ct);

            foreach (var v in chunk)
            {
                if (existing.Contains(v.ItemId)) db.ItemVolumes.Update(v);
                else db.ItemVolumes.Add(v);
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            saved += chunk.Length;
            progress?.Report(new SyncProgress("Saving volume", saved, volumes.Count));
        }
    }

    /// <summary>sold/day = mean over complete days (the current UTC day is still forming); supply = latest day.</summary>
    private static ItemVolume BuildVolume(int itemId, IEnumerable<Datawars2HistoryDto> history)
    {
        var ordered = history.OrderBy(r => r.Date).ToList();
        var today = DateTime.UtcNow.Date;
        var complete = ordered.Where(r => r.Date.Date < today).ToList();
        var basis = complete.Count > 0 ? complete : ordered;
        var latest = ordered[^1];

        return new ItemVolume
        {
            ItemId = itemId,
            SoldPerDay = (int)Math.Round(basis.Average(r => (double)r.SellSold)),
            BoughtPerDay = (int)Math.Round(basis.Average(r => (double)r.BuySold)),
            SupplyNow = (int)Math.Round(latest.SellQuantityAvg),
            DemandNow = (int)Math.Round(latest.BuyQuantityAvg),
            UpdatedUtc = DateTime.UtcNow
        };
    }

    private static async Task UpdateSyncStateAsync(AppDbContext db, string key, int count, CancellationToken ct)
    {
        var state = await db.SyncStates.FindAsync(new object?[] { key }, ct);
        if (state is null)
            db.SyncStates.Add(new SyncState { Key = key, LastSyncedUtc = DateTime.UtcNow, RecordCount = count });
        else { state.LastSyncedUtc = DateTime.UtcNow; state.RecordCount = count; }
        await db.SaveChangesAsync(ct);
    }
}
