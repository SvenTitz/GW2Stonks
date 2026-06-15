using GW2Stonks.Data;
using GW2Stonks.Data.Entities;
using GW2Stonks.Gw2Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GW2Stonks.Services;

/// <summary>
/// Pulls the GW2 catalog (items, recipes) and trading-post prices from the API into MariaDB.
/// A single process-wide gate serialises syncs so the timer and a manual click never overlap.
/// </summary>
public sealed class Gw2SyncService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly Gw2ApiClient _api;
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly Gw2Options _options;
    private readonly ILogger<Gw2SyncService> _log;

    public Gw2SyncService(
        Gw2ApiClient api,
        IDbContextFactory<AppDbContext> dbf,
        IOptions<Gw2Options> options,
        ILogger<Gw2SyncService> log)
    {
        _api = api;
        _dbf = dbf;
        _options = options.Value;
        _log = log;
    }

    /// <summary>Sync items then recipes.</summary>
    public async Task SyncCatalogAsync(IProgress<SyncProgress>? progress = null, CancellationToken ct = default)
    {
        await SyncItemsAsync(progress, ct);
        await SyncRecipesAsync(progress, ct);
    }

    public Task<int> SyncItemsAsync(IProgress<SyncProgress>? progress = null, CancellationToken ct = default) =>
        WithGateAsync(() => SyncItemsCoreAsync(progress, ct), ct);

    public Task<int> SyncRecipesAsync(IProgress<SyncProgress>? progress = null, CancellationToken ct = default) =>
        WithGateAsync(() => SyncRecipesCoreAsync(progress, ct), ct);

    /// <summary>Refresh prices, waiting for the gate if another sync is running.</summary>
    public Task<int> RefreshPricesAsync(IProgress<SyncProgress>? progress = null, CancellationToken ct = default) =>
        WithGateAsync(() => RefreshPricesCoreAsync(progress, ct), ct);

    /// <summary>Refresh prices only if no sync is currently running; returns null if it was skipped.</summary>
    public async Task<int?> TryRefreshPricesAsync(CancellationToken ct = default)
    {
        if (!await Gate.WaitAsync(0, ct)) return null;
        try { return await RefreshPricesCoreAsync(null, ct); }
        finally { Gate.Release(); }
    }

    public async Task<DashboardStatus> GetStatusAsync(CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var states = await db.SyncStates.AsNoTracking().ToDictionaryAsync(s => s.Key, ct);
        return new DashboardStatus(
            await db.Items.CountAsync(ct),
            await db.Recipes.CountAsync(ct),
            await db.Prices.CountAsync(ct),
            states);
    }

    // ── Core implementations (no gate) ──────────────────────────────────────

    private async Task<int> SyncItemsCoreAsync(IProgress<SyncProgress>? progress, CancellationToken ct)
    {
        var ids = await _api.GetItemIdsAsync(ct);
        var dtos = await FetchAllAsync(ids, _api.GetItemsAsync, "Fetching items", progress, ct);

        await using var db = await _dbf.CreateDbContextAsync(ct);
        int saved = 0;
        progress?.Report(new SyncProgress("Saving items", 0, dtos.Count));
        foreach (var chunk in dtos.Chunk(1000))
        {
            var chunkIds = chunk.Select(d => d.Id).ToList();
            var existing = await db.Items.AsNoTracking()
                .Where(i => chunkIds.Contains(i.Id))
                .Select(i => i.Id)
                .ToHashSetAsync(ct);

            foreach (var d in chunk)
            {
                var entity = new Item
                {
                    Id = d.Id,
                    // GW2 names sometimes carry stray leading/trailing whitespace (e.g. " Mastery Point"),
                    // which breaks alphabetical sorting and display — trim it.
                    Name = Truncate(d.Name.Trim(), 200),
                    Type = Truncate(d.Type, 50),
                    Rarity = Truncate(d.Rarity, 30),
                    Level = d.Level,
                    VendorValue = d.VendorValue,
                    IconUrl = d.Icon,
                    Flags = Truncate(string.Join(',', d.Flags), 255)
                };
                if (existing.Contains(d.Id)) db.Items.Update(entity);
                else db.Items.Add(entity);
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            saved += chunk.Length;
            progress?.Report(new SyncProgress("Saving items", saved, dtos.Count));
        }

        await UpdateSyncStateAsync(db, "items", dtos.Count, ct);
        _log.LogInformation("Synced {Count} items", dtos.Count);
        return dtos.Count;
    }

    private async Task<int> SyncRecipesCoreAsync(IProgress<SyncProgress>? progress, CancellationToken ct)
    {
        var ids = await _api.GetRecipeIdsAsync(ct);
        var dtos = await FetchAllAsync(ids, _api.GetRecipesAsync, "Fetching recipes", progress, ct);

        await using var db = await _dbf.CreateDbContextAsync(ct);
        var itemIds = await db.Items.AsNoTracking().Select(i => i.Id).ToHashSetAsync(ct);

        // Drop recipes that reference items not in our catalog, to respect the FKs.
        var valid = dtos
            .Where(r => itemIds.Contains(r.OutputItemId) && r.Ingredients.All(g => itemIds.Contains(g.ItemId)))
            .ToList();
        int skipped = dtos.Count - valid.Count;
        if (skipped > 0) _log.LogInformation("Skipped {Skipped} recipes referencing unknown items", skipped);

        progress?.Report(new SyncProgress("Saving recipes", 0, valid.Count));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.RecipeIngredients.ExecuteDeleteAsync(ct);
        await db.Recipes.ExecuteDeleteAsync(ct);

        int saved = 0;
        foreach (var chunk in valid.Chunk(1000))
        {
            foreach (var r in chunk)
            {
                db.Recipes.Add(new Recipe
                {
                    Id = r.Id,
                    Type = Truncate(r.Type, 50),
                    OutputItemId = r.OutputItemId,
                    OutputItemCount = r.OutputItemCount,
                    MinRating = r.MinRating,
                    TimeToCraftMs = r.TimeToCraftMs,
                    Disciplines = Truncate(string.Join(',', r.Disciplines), 255),
                    Flags = Truncate(string.Join(',', r.Flags), 255),
                    Ingredients = r.Ingredients
                        .Select(g => new RecipeIngredient { ItemId = g.ItemId, Count = g.Count })
                        .ToList()
                });
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            saved += chunk.Length;
            progress?.Report(new SyncProgress("Saving recipes", saved, valid.Count));
        }

        await UpdateSyncStateAsync(db, "recipes", valid.Count, ct);
        await tx.CommitAsync(ct);
        _log.LogInformation("Synced {Count} recipes ({Skipped} skipped)", valid.Count, skipped);
        return valid.Count;
    }

    private async Task<int> RefreshPricesCoreAsync(IProgress<SyncProgress>? progress, CancellationToken ct)
    {
        var ids = await _api.GetPriceIdsAsync(ct);
        var dtos = await FetchAllAsync(ids, _api.GetPricesAsync, "Fetching prices", progress, ct);

        await using var db = await _dbf.CreateDbContextAsync(ct);
        var itemIds = await db.Items.AsNoTracking().Select(i => i.Id).ToHashSetAsync(ct);
        var now = DateTime.UtcNow;

        var rows = dtos
            .Where(p => itemIds.Contains(p.Id))
            .Select(p => new Price
            {
                ItemId = p.Id,
                BuyUnitPrice = p.Buys?.UnitPrice ?? 0,
                BuyQuantity = p.Buys?.Quantity ?? 0,
                SellUnitPrice = p.Sells?.UnitPrice ?? 0,
                SellQuantity = p.Sells?.Quantity ?? 0,
                UpdatedUtc = now
            })
            .ToList();

        progress?.Report(new SyncProgress("Saving prices", 0, rows.Count));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Prices.ExecuteDeleteAsync(ct);

        int saved = 0;
        foreach (var chunk in rows.Chunk(1000))
        {
            db.Prices.AddRange(chunk);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            saved += chunk.Length;
            progress?.Report(new SyncProgress("Saving prices", saved, rows.Count));
        }

        await UpdateSyncStateAsync(db, "prices", rows.Count, ct);
        await tx.CommitAsync(ct);
        _log.LogInformation("Refreshed {Count} prices", rows.Count);
        return rows.Count;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetch all ids in parallel, batched at the configured size, reporting progress.
    /// Each batch retries transient failures independently. Crucially, batch errors are
    /// caught inside the loop so a single failure never faults <see cref="Parallel.ForEachAsync"/>
    /// (which would cancel — and abort the in-flight sockets of — every sibling request).
    /// If a batch still fails after retries, the whole fetch is aborted with an aggregated error;
    /// callers fetch fully before touching the DB, so existing data is left intact on failure.
    /// </summary>
    private async Task<List<T>> FetchAllAsync<T>(
        IReadOnlyList<int> ids,
        Func<IEnumerable<int>, CancellationToken, Task<List<T>>> fetchBatch,
        string phase,
        IProgress<SyncProgress>? progress,
        CancellationToken ct)
    {
        var batches = ids.Chunk(_options.BatchSize).ToList();
        var results = new List<T>(ids.Count);
        var failures = new List<Exception>();
        int done = 0;
        var gate = new object();
        progress?.Report(new SyncProgress(phase, 0, ids.Count));

        await Parallel.ForEachAsync(
            batches,
            new ParallelOptions { MaxDegreeOfParallelism = _options.MaxConcurrency, CancellationToken = ct },
            // Use the caller's token (ct), not the loop's internal token, so a sibling fault
            // cannot cancel this request mid-flight.
            async (batch, _) =>
            {
                try
                {
                    var part = await FetchBatchWithRetryAsync(batch, fetchBatch, ct);
                    lock (gate)
                    {
                        results.AddRange(part);
                        done += batch.Length;
                        progress?.Report(new SyncProgress(phase, done, ids.Count));
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    lock (gate) failures.Add(ex);
                }
            });

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"{failures.Count} of {batches.Count} batch(es) failed during '{phase}' after retries. " +
                $"First error: {failures[0].Message}",
                failures[0]);
        }

        return results;
    }

    /// <summary>Fetch a single batch, retrying transient errors with a short backoff.</summary>
    private async Task<List<T>> FetchBatchWithRetryAsync<T>(
        int[] batch,
        Func<IEnumerable<int>, CancellationToken, Task<List<T>>> fetchBatch,
        CancellationToken ct)
    {
        const int maxAttempts = 4;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await fetchBatch(batch, ct);
            }
            catch (Exception ex) when (attempt < maxAttempts && !ct.IsCancellationRequested)
            {
                _log.LogWarning("Batch fetch attempt {Attempt}/{Max} failed: {Error}. Retrying…",
                    attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), ct);
            }
        }
    }

    private async Task<T> WithGateAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try { return await action(); }
        finally { Gate.Release(); }
    }

    private static async Task UpdateSyncStateAsync(AppDbContext db, string key, int count, CancellationToken ct)
    {
        var state = await db.SyncStates.FindAsync(new object?[] { key }, ct);
        if (state is null)
        {
            db.SyncStates.Add(new SyncState { Key = key, LastSyncedUtc = DateTime.UtcNow, RecordCount = count });
        }
        else
        {
            state.LastSyncedUtc = DateTime.UtcNow;
            state.RecordCount = count;
        }
        await db.SaveChangesAsync(ct);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
