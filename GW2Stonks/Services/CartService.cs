using GW2Stonks.Data;
using GW2Stonks.Data.Entities;
using GW2Stonks.Models;
using Microsoft.EntityFrameworkCore;

namespace GW2Stonks.Services;

/// <summary>
/// The "to be crafted" cart — an app-wide list of items + quantities to craft. Held in memory and
/// written through to the <see cref="CartEntry"/> table so it survives restarts (loaded once at
/// startup by <see cref="CartLoader"/>). Raises <see cref="OnChange"/> so the nav badge and planner
/// page stay in sync.
/// </summary>
public sealed class CartService
{
    private readonly object _lock = new();
    private readonly Dictionary<int, CartItem> _items = new();
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly ILogger<CartService> _log;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public CartService(IDbContextFactory<AppDbContext> dbf, ILogger<CartService> log)
    {
        _dbf = dbf;
        _log = log;
    }

    public event Action? OnChange;

    public IReadOnlyList<CartItem> Items
    {
        get { lock (_lock) return _items.Values.OrderBy(i => i.Name).ToList(); }
    }

    public int Count { get { lock (_lock) return _items.Count; } }

    /// <summary>Quantity of an item currently in the cart (0 if not present).</summary>
    public int GetQuantity(int itemId)
    {
        lock (_lock) return _items.TryGetValue(itemId, out var item) ? item.Quantity : 0;
    }

    public void Add(int itemId, string name, string? iconUrl, int quantity)
    {
        if (quantity <= 0) return;
        lock (_lock)
        {
            if (_items.TryGetValue(itemId, out var existing))
                existing.Quantity += quantity;
            else
                _items[itemId] = new CartItem { ItemId = itemId, Name = name, IconUrl = iconUrl, Quantity = quantity };
        }
        Changed();
    }

    public void SetQuantity(int itemId, int quantity)
    {
        if (quantity <= 0) { Remove(itemId); return; }
        lock (_lock)
        {
            if (_items.TryGetValue(itemId, out var existing)) existing.Quantity = quantity;
        }
        Changed();
    }

    public void Remove(int itemId)
    {
        bool removed;
        lock (_lock) removed = _items.Remove(itemId);
        if (removed) Changed();
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (_items.Count == 0) return;
            _items.Clear();
        }
        Changed();
    }

    /// <summary>Snapshot of item id → quantity, for plan building.</summary>
    public IReadOnlyDictionary<int, int> ToQuantities()
    {
        lock (_lock) return _items.ToDictionary(kv => kv.Key, kv => kv.Value.Quantity);
    }

    /// <summary>Load the persisted cart from the database (called once at startup).</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var entries = await ReadEntriesAsync(ct);
        lock (_lock)
        {
            _items.Clear();
            foreach (var e in entries)
                _items[e.ItemId] = new CartItem { ItemId = e.ItemId, Name = e.Name, IconUrl = e.IconUrl, Quantity = e.Quantity };
        }
        OnChange?.Invoke();
    }

    private async Task<List<CartEntry>> ReadEntriesAsync(CancellationToken ct)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.CartEntries.AsNoTracking().ToListAsync(ct);
    }

    /// <summary>Persist the cart now and await completion (the normal mutators persist in the background).</summary>
    public Task SaveAsync() => PersistAsync();

    private void Changed()
    {
        OnChange?.Invoke();
        _ = PersistAsync();
    }

    /// <summary>Write the current cart to the DB (full replace), serialised so writes don't overlap.</summary>
    private async Task PersistAsync()
    {
        await _writeGate.WaitAsync();
        try
        {
            List<CartItem> snapshot;
            lock (_lock) snapshot = _items.Values.ToList();

            await using var db = await _dbf.CreateDbContextAsync();
            await db.CartEntries.ExecuteDeleteAsync();
            db.CartEntries.AddRange(snapshot.Select(i => new CartEntry
            {
                ItemId = i.ItemId,
                Name = i.Name,
                IconUrl = i.IconUrl,
                Quantity = i.Quantity
            }));
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not persist cart");
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
