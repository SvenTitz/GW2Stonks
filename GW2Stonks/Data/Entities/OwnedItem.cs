namespace GW2Stonks.Data.Entities;

/// <summary>
/// A material the account already owns, summed across material storage, the bank, the shared
/// inventory and every character's bags. Refreshed on demand from the GW2 account API and used
/// by the planner to subtract stock from the shopping list. No FK to <see cref="Item"/> on
/// purpose — an inventory can briefly hold an item id the catalog hasn't synced yet.
/// </summary>
public class OwnedItem
{
    public int ItemId { get; set; }

    /// <summary>Total count owned across all checked storage locations.</summary>
    public int Count { get; set; }
}
