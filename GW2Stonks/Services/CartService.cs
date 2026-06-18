using GW2Stonks.Models;

namespace GW2Stonks.Services;

/// <summary>
/// The "to be crafted" cart — an in-memory, app-wide list of items + quantities to craft.
/// Raises <see cref="OnChange"/> so the nav badge and planner page stay in sync.
/// </summary>
public sealed class CartService
{
    private readonly object _lock = new();
    private readonly Dictionary<int, CartItem> _items = new();

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
        OnChange?.Invoke();
    }

    public void SetQuantity(int itemId, int quantity)
    {
        if (quantity <= 0) { Remove(itemId); return; }
        lock (_lock)
        {
            if (_items.TryGetValue(itemId, out var existing)) existing.Quantity = quantity;
        }
        OnChange?.Invoke();
    }

    public void Remove(int itemId)
    {
        bool removed;
        lock (_lock) removed = _items.Remove(itemId);
        if (removed) OnChange?.Invoke();
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (_items.Count == 0) return;
            _items.Clear();
        }
        OnChange?.Invoke();
    }

    /// <summary>Snapshot of item id → quantity, for plan building.</summary>
    public IReadOnlyDictionary<int, int> ToQuantities()
    {
        lock (_lock) return _items.ToDictionary(kv => kv.Key, kv => kv.Value.Quantity);
    }
}
