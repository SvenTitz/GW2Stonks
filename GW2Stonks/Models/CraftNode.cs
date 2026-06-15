namespace GW2Stonks.Models;

/// <summary>A node in a craft breakdown tree: an item, how many are needed here, and the buy-vs-craft call.</summary>
public sealed class CraftNode
{
    public int ItemId { get; set; }
    public string Name { get; set; } = "";
    public string? IconUrl { get; set; }

    /// <summary>How many of this item are needed at this position in the tree.</summary>
    public int Count { get; set; }

    /// <summary>Units produced per craft of this item (relevant when Decision == "Craft").</summary>
    public int OutputCount { get; set; } = 1;

    public int? UnitBest { get; set; }
    public int? UnitBuy { get; set; }
    public int? UnitCraft { get; set; }

    /// <summary>"Buy", "Craft", or "—" (unobtainable).</summary>
    public string Decision { get; set; } = "";

    public List<CraftNode> Children { get; } = new();

    /// <summary>Unit cost of the chosen decision (craft cost if crafting, buy price if buying).</summary>
    public int? UnitEffective => Decision switch
    {
        "Craft" => UnitCraft,
        "Buy" => UnitBuy,
        _ => UnitBest
    };

    /// <summary>Effective unit cost × Count — the total this node contributes to its parent.</summary>
    public int? Subtotal => UnitEffective is null ? null : UnitEffective * Count;
}
