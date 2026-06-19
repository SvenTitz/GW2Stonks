using GW2Stonks.Data;
using GW2Stonks.Data.Entities;
using GW2Stonks.Gw2Api;
using Microsoft.EntityFrameworkCore;

namespace GW2Stonks.Services;

/// <summary>Snapshot of the account-integration state for the Settings page.</summary>
public sealed record AccountStatus(bool HasKey, string? AccountName, DateTime? OwnedUpdatedUtc, int OwnedItemTypes);

/// <summary>
/// Owns the GW2 account API key (persisted in <see cref="AppSetting"/> so it survives restarts)
/// and refreshes the <see cref="OwnedItem"/> cache by summing the account's material storage,
/// bank, shared inventory and every character's bags. The planner subtracts that stock from the
/// shopping list.
/// </summary>
public sealed class AccountService
{
    public const string ApiKeySettingKey = "gw2.apikey";
    private const string AccountNameSettingKey = "gw2.account";
    private const string OwnedSyncKey = "owned";

    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly Gw2ApiClient _api;
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly ILogger<AccountService> _log;

    public AccountService(Gw2ApiClient api, IDbContextFactory<AppDbContext> dbf, ILogger<AccountService> log)
    {
        _api = api;
        _dbf = dbf;
        _log = log;
    }

    // ── API key persistence ─────────────────────────────────────────────────

    public async Task<string?> GetApiKeyAsync(CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var value = await db.AppSettings.AsNoTracking()
            .Where(s => s.Key == ApiKeySettingKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public async Task SaveApiKeyAsync(string? key, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var existing = await db.AppSettings.FindAsync(new object?[] { ApiKeySettingKey }, ct);

        if (string.IsNullOrWhiteSpace(key))
        {
            if (existing is not null) db.AppSettings.Remove(existing);
        }
        else if (existing is null)
        {
            db.AppSettings.Add(new AppSetting { Key = ApiKeySettingKey, Value = key.Trim() });
        }
        else
        {
            existing.Value = key.Trim();
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Validate a key against /v2/tokeninfo; null if it's rejected.</summary>
    public async Task<Gw2TokenInfoDto?> ValidateAsync(string key, CancellationToken ct = default)
    {
        try { return await _api.GetTokenInfoAsync(key, ct); }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "Token validation failed");
            return null;
        }
    }

    // ── Owned-stock cache ───────────────────────────────────────────────────

    /// <summary>Item id → quantity currently owned across all checked storage locations.</summary>
    public async Task<IReadOnlyDictionary<int, int>> GetOwnedAsync(CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.OwnedItems.AsNoTracking().ToDictionaryAsync(o => o.ItemId, o => o.Count, ct);
    }

    public async Task<AccountStatus> GetStatusAsync(CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var settings = await db.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        var hasKey = settings.TryGetValue(ApiKeySettingKey, out var k) && !string.IsNullOrWhiteSpace(k);
        settings.TryGetValue(AccountNameSettingKey, out var name);

        var ownedTypes = await db.OwnedItems.CountAsync(ct);
        var updated = await db.SyncStates.AsNoTracking()
            .Where(s => s.Key == OwnedSyncKey).Select(s => (DateTime?)s.LastSyncedUtc).FirstOrDefaultAsync(ct);

        return new AccountStatus(hasKey, string.IsNullOrWhiteSpace(name) ? null : name, updated, ownedTypes);
    }

    /// <summary>
    /// Re-read the account's stock from the GW2 API and replace the owned-item cache.
    /// Returns the number of distinct item types owned. Throws if no key is set, the key is
    /// invalid, or it lacks the 'inventories' permission.
    /// </summary>
    public async Task<int> RefreshOwnedAsync(IProgress<SyncProgress>? progress = null, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try { return await RefreshCoreAsync(progress, ct); }
        finally { Gate.Release(); }
    }

    private async Task<int> RefreshCoreAsync(IProgress<SyncProgress>? progress, CancellationToken ct)
    {
        var key = await GetApiKeyAsync(ct)
            ?? throw new InvalidOperationException("No API key saved. Add one on the Settings page first.");

        progress?.Report(new SyncProgress("Checking key", 0, 1));
        var token = await _api.GetTokenInfoAsync(key, ct)
            ?? throw new InvalidOperationException("The saved API key was rejected by the GW2 API.");

        var perms = token.Permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!perms.Contains("inventories"))
            throw new InvalidOperationException("The API key needs the 'inventories' permission to read your stock.");

        var owned = new Dictionary<int, int>();
        void AddSlot(int id, int count)
        {
            if (count > 0) owned[id] = owned.GetValueOrDefault(id) + count;
        }

        progress?.Report(new SyncProgress("Material storage", 0, 1));
        foreach (var m in await _api.GetMaterialsAsync(key, ct)) AddSlot(m.Id, m.Count);

        progress?.Report(new SyncProgress("Bank", 0, 1));
        await TryAddSlotsAsync("bank", AddSlot, () => _api.GetBankAsync(key, ct));

        progress?.Report(new SyncProgress("Shared inventory", 0, 1));
        await TryAddSlotsAsync("shared inventory", AddSlot, () => _api.GetSharedInventoryAsync(key, ct));

        if (perms.Contains("characters"))
        {
            List<string> names;
            try { names = await _api.GetCharacterNamesAsync(key, ct); }
            catch (Exception ex) { _log.LogWarning(ex, "Could not list characters; skipping character bags"); names = new(); }

            for (var i = 0; i < names.Count; i++)
            {
                progress?.Report(new SyncProgress($"Character bags ({i + 1}/{names.Count})", i, names.Count));
                try
                {
                    var inv = await _api.GetCharacterInventoryAsync(key, names[i], ct);
                    if (inv is null) continue;
                    foreach (var bag in inv.Bags)
                        if (bag is not null)
                            foreach (var slot in bag.Inventory)
                                if (slot is not null) AddSlot(slot.Id, slot.Count);
                }
                catch (Exception ex) { _log.LogWarning(ex, "Could not read inventory for {Char}; skipping", names[i]); }
            }
        }

        await ReplaceOwnedAsync(owned, ct);
        await SaveAccountNameAsync(token.Name, ct);
        _log.LogInformation("Refreshed owned stock: {Types} item types", owned.Count);
        return owned.Count;
    }

    private async Task TryAddSlotsAsync(string what, Action<int, int> add, Func<Task<List<Gw2ItemSlotDto?>>> fetch)
    {
        try
        {
            foreach (var slot in await fetch())
                if (slot is not null) add(slot.Id, slot.Count);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Could not read {What}; skipping", what); }
    }

    private async Task ReplaceOwnedAsync(Dictionary<int, int> owned, CancellationToken ct)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        await db.OwnedItems.ExecuteDeleteAsync(ct);
        db.OwnedItems.AddRange(owned.Select(kv => new OwnedItem { ItemId = kv.Key, Count = kv.Value }));
        await db.SaveChangesAsync(ct);

        var state = await db.SyncStates.FindAsync(new object?[] { OwnedSyncKey }, ct);
        if (state is null)
            db.SyncStates.Add(new SyncState { Key = OwnedSyncKey, LastSyncedUtc = DateTime.UtcNow, RecordCount = owned.Count });
        else { state.LastSyncedUtc = DateTime.UtcNow; state.RecordCount = owned.Count; }
        await db.SaveChangesAsync(ct);
    }

    private async Task SaveAccountNameAsync(string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var existing = await db.AppSettings.FindAsync(new object?[] { AccountNameSettingKey }, ct);
        if (existing is null) db.AppSettings.Add(new AppSetting { Key = AccountNameSettingKey, Value = name });
        else existing.Value = name;
        await db.SaveChangesAsync(ct);
    }
}
