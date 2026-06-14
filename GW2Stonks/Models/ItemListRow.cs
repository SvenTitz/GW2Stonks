namespace GW2Stonks.Models;

/// <summary>Flat projection of an item + its prices for the items grid (server-side bound).</summary>
public sealed class ItemListRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Rarity { get; set; } = "";
    public int Level { get; set; }
    public string? IconUrl { get; set; }

    /// <summary>Highest buy order (instant-sell price), null if the item isn't on the trading post.</summary>
    public int? Buy { get; set; }

    /// <summary>Lowest sell listing (instant-buy price), null if the item isn't on the trading post.</summary>
    public int? Sell { get; set; }

    public DateTime? PriceUpdatedUtc { get; set; }
}
