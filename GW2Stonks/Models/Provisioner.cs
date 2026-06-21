namespace GW2Stonks.Models;

/// <summary>One thing traded in (an item + how many of it) to earn a Provisioner Token.</summary>
public sealed record ProvisionerOffer(string ItemName, int Quantity);

/// <summary>One faction tab on a provisioner: a list of items, any of which earns a token (1 each).</summary>
public sealed class ProvisionerTab
{
    public string Name { get; init; } = "";
    public IReadOnlyList<string> Items { get; init; } = new List<string>();
}

/// <summary>A Heart of Maguuma Faction Provisioner and its faction tabs.</summary>
public sealed class ProvisionerVendor
{
    public string Name { get; init; } = "";
    public string Zone { get; init; } = "";
    public string? Waypoint { get; init; }
    public string? WaypointChatLink { get; init; }

    /// <summary>Trade limit per tab, e.g. "7 per week".</summary>
    public string Limit { get; init; } = "";

    public IReadOnlyList<ProvisionerTab> Tabs { get; init; } = new List<ProvisionerTab>();
}

// ── Priced views (built live from the catalog) ──────────────────────────────

/// <summary>An item priced for "cheapest to buy" — used for daily materials and tab options.</summary>
public sealed class PricedRow
{
    public string ItemName { get; set; } = "";
    public int? ItemId { get; set; }
    public string? IconUrl { get; set; }

    /// <summary>How many you trade for one token (1 for faction items, a stack for daily materials).</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Unit price: for faction items, the average of the 7 cheapest sell listings (what you'd pay
    /// per unit buying all 7); for daily materials, the single sell-listing price. Null if untradable.
    /// </summary>
    public int? UnitPrice { get; set; }

    /// <summary>UnitPrice × Quantity — copper for one token via this item (used for daily materials).</summary>
    public int? CostPerToken { get; set; }

    /// <summary>Cost to buy 7 (the week's worth) — the sum of the 7 cheapest sell listings. Faction items only.</summary>
    public int? Weekly7Cost { get; set; }

    public bool Buyable => UnitPrice is not null;
}

/// <summary>The cheapest item to buy in one tab (you buy it 7× for the weekly tokens).</summary>
public sealed class TabBest
{
    public string Tab { get; set; } = "";
    public string? ItemName { get; set; }
    public int? ItemId { get; set; }
    public string? IconUrl { get; set; }

    /// <summary>Cheapest unit (sell-listing) price among the tab's items.</summary>
    public int? UnitPrice { get; set; }

    /// <summary>UnitPrice × 7 — cost to buy the week's worth from this tab.</summary>
    public int? WeeklyCost { get; set; }

    /// <summary>All the tab's items priced, cheapest first (for reference / drill-down).</summary>
    public IReadOnlyList<PricedRow> Options { get; set; } = new List<PricedRow>();
}

/// <summary>A provisioner with the cheapest pick per faction tab.</summary>
public sealed class VendorView
{
    public string Name { get; set; } = "";
    public string Zone { get; set; } = "";
    public string? Waypoint { get; set; }
    public string? WaypointChatLink { get; set; }
    public string Limit { get; set; } = "";
    public IReadOnlyList<TabBest> Tabs { get; set; } = new List<TabBest>();

    /// <summary>Sum of the cheapest 7× across all tabs — total to buy this vendor's weekly tokens.</summary>
    public int? WeeklyTotal { get; set; }
}

/// <summary>Everything the Provisioner page shows: HoM vendors + the daily-rotation materials.</summary>
public sealed record ProvisionerView(IReadOnlyList<VendorView> Vendors, IReadOnlyList<PricedRow> DailyMaterials);
