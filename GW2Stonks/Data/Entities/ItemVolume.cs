namespace GW2Stonks.Data.Entities;

/// <summary>
/// Cached trading-post liquidity for an item, sourced from datawars2.ie's daily aggregates.
/// All quantities are unit counts.
/// </summary>
public class ItemVolume
{
    /// <summary>Item these figures belong to (also the primary key).</summary>
    public int ItemId { get; set; }

    /// <summary>Units bought off sellers per day (how fast a listing sells), averaged over recent days.</summary>
    public int SoldPerDay { get; set; }

    /// <summary>Units sold into buy orders per day, averaged over recent days.</summary>
    public int BoughtPerDay { get; set; }

    /// <summary>Total quantity listed for sale (supply / your queue).</summary>
    public int SupplyNow { get; set; }

    /// <summary>Total quantity wanted by buy orders (demand).</summary>
    public int DemandNow { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public Item? Item { get; set; }
}
