using System.ComponentModel.DataAnnotations.Schema;

namespace GW2Stonks.Data.Entities;

/// <summary>A Guild Wars 2 item, as returned by <c>/v2/items</c>.</summary>
public class Item
{
    /// <summary>GW2 item id (assigned by the API, not generated locally).</summary>
    public int Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Item type, e.g. "CraftingMaterial", "Armor", "Weapon".</summary>
    public string Type { get; set; } = "";

    public string Rarity { get; set; } = "";

    /// <summary>Required character level.</summary>
    public int Level { get; set; }

    /// <summary>Price the item sells to an NPC vendor for, in copper.</summary>
    public int VendorValue { get; set; }

    public string? IconUrl { get; set; }

    /// <summary>Comma-separated item flags (e.g. "AccountBound,NoSell,SoulbindOnAcquire").</summary>
    public string Flags { get; set; } = "";

    /// <summary>True when the item can be traded on the trading post (no account-bind flags).</summary>
    [NotMapped]
    public bool IsTradable =>
        !Flags.Contains("AccountBound") && !Flags.Contains("SoulbindOnAcquire");

    /// <summary>Latest trading-post prices for this item, if it is tradable and has been synced.</summary>
    public Price? Price { get; set; }

    /// <summary>Recipes that produce this item.</summary>
    public ICollection<Recipe> CraftedBy { get; set; } = new List<Recipe>();
}
