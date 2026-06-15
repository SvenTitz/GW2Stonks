namespace GW2Stonks.Models;

/// <summary>A craftable, sellable item with its computed craft profit (one pricing mode). Copper.</summary>
public sealed class ProfitRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? IconUrl { get; set; }
    public string Type { get; set; } = "";
    public string Rarity { get; set; } = "";
    public string Disciplines { get; set; } = "";
    public int MinRating { get; set; }
    public int OutputCount { get; set; }

    /// <summary>Cheapest way to just buy this item (TP in the chosen mode, or vendor).</summary>
    public int? BuyPrice { get; set; }

    /// <summary>Best per-unit cost to craft it (recursively choosing buy-vs-craft per ingredient).</summary>
    public int? CraftCost { get; set; }

    /// <summary>What you receive selling it, after the 15% trading-post tax.</summary>
    public int? NetSell { get; set; }

    /// <summary>NetSell − CraftCost. Negative means crafting loses money.</summary>
    public int? Profit { get; set; }

    /// <summary>Profit as a percentage of craft cost.</summary>
    public double? Margin { get; set; }

    /// <summary>True when crafting is cheaper than simply buying the item.</summary>
    public bool CraftCheaperThanBuy { get; set; }

    /// <summary>Estimated units bought off sellers per day (liquidity). Null if not cached yet.</summary>
    public int? SoldPerDay { get; set; }

    /// <summary>Days for the current listed supply to clear at the sold/day rate. Lower = sells sooner.</summary>
    public double? SellThroughDays { get; set; }
}
