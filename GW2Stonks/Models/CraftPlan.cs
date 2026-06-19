namespace GW2Stonks.Models;

/// <summary>Where a material is bought.</summary>
public enum BuySource { TradingPost, Vendor }

/// <summary>One line of the shopping list — a material to buy and from where.</summary>
public sealed class ShoppingLine
{
    public int ItemId { get; set; }
    public string Name { get; set; } = "";
    public string? IconUrl { get; set; }
    public int Quantity { get; set; }
    public int? UnitPrice { get; set; }
    public int? TotalPrice { get; set; }
    public BuySource Source { get; set; }
}

/// <summary>One crafting step — how many of an item to craft, and with which discipline.</summary>
public sealed class CraftStep
{
    public int ItemId { get; set; }
    public string Name { get; set; } = "";
    public string? IconUrl { get; set; }

    /// <summary>Units produced (a multiple of the recipe's output count).</summary>
    public int Quantity { get; set; }

    /// <summary>How many times the recipe is run.</summary>
    public int Crafts { get; set; }

    public int OutputCount { get; set; }

    /// <summary>Discipline chosen to group the step (picked to minimise discipline switching).</summary>
    public string Discipline { get; set; } = "";

    /// <summary>All disciplines that can craft this, for reference.</summary>
    public string Disciplines { get; set; } = "";

    /// <summary>True when this is a cart item (a final product to sell), not an intermediate.</summary>
    public bool IsFinal { get; set; }

    /// <summary>Depth in the BOM (deeper sub-components are crafted first).</summary>
    public int Depth { get; set; }
}

/// <summary>Per-item cost/revenue/profit for a cart line (a final product being crafted to sell).</summary>
public sealed class CartLine
{
    public int ItemId { get; set; }
    public string Name { get; set; } = "";
    public string? IconUrl { get; set; }
    public int Quantity { get; set; }
    public int? Cost { get; set; }
    public int? Revenue { get; set; }
    public int? Profit { get; set; }
}

/// <summary>One material drawn from owned stock instead of bought/crafted.</summary>
public sealed class OwnedUsedLine
{
    public int ItemId { get; set; }
    public string Name { get; set; } = "";
    public string? IconUrl { get; set; }

    /// <summary>Units taken from stock.</summary>
    public int Quantity { get; set; }

    /// <summary>Copper saved (units × the item's cheapest acquisition cost).</summary>
    public int? Value { get; set; }
}

/// <summary>A full craft plan for a cart: what to buy and what to craft.</summary>
public sealed class CraftPlan
{
    public List<CartLine> CartLines { get; set; } = new();
    public List<ShoppingLine> Shopping { get; set; } = new();
    public List<CraftStep> Steps { get; set; } = new();
    public List<OwnedUsedLine> OwnedUsed { get; set; } = new();

    /// <summary>Total cost of all materials to buy.</summary>
    public int TotalBuyCost { get; set; }

    /// <summary>Total revenue from selling the final products (after tax).</summary>
    public int TotalRevenue { get; set; }

    /// <summary>TotalRevenue − TotalBuyCost.</summary>
    public int TotalProfit { get; set; }

    /// <summary>Distinct item types drawn from owned stock instead of bought/crafted.</summary>
    public int OwnedTypesApplied { get; set; }

    /// <summary>Copper value of owned stock applied (each used unit valued at its cheapest source).</summary>
    public int OwnedSavings { get; set; }
}
