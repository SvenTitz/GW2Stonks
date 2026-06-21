namespace GW2Stonks.Models;

/// <summary>
/// One of the account's active sell listings, with how much cheaper supply sits ahead of it on the
/// trading post and how that compares to the item's daily sales (so you know when to relist).
/// </summary>
public sealed class SellListingRow
{
    public int ItemId { get; set; }
    public string Name { get; set; } = "";
    public string? IconUrl { get; set; }

    /// <summary>My listed unit price (copper).</summary>
    public int Price { get; set; }

    /// <summary>How many units I have listed at this price.</summary>
    public int Quantity { get; set; }

    /// <summary>Current cheapest sell price on the TP (the competition; may be my own listing).</summary>
    public int? LowestSell { get; set; }

    /// <summary>Units listed strictly cheaper than mine — they sell before mine do.</summary>
    public int UnitsAhead { get; set; }

    /// <summary>Cached daily sales (units/day); null if no volume data.</summary>
    public int? SoldPerDay { get; set; }

    /// <summary>UnitsAhead as a percent of daily sales; null when sold/day is unknown.</summary>
    public double? AheadVsDailyPct { get; set; }
}
