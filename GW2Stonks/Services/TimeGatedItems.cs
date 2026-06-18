namespace GW2Stonks.Services;

/// <summary>
/// Daily time-gated crafting materials — each limited to ~one craft per day. Per the user's rule
/// these are never crafted, always bought; and any recipe that directly requires one is disallowed,
/// so its output (e.g. Deldrimor Steel Ingot, which needs Lump of Mithrillium) also becomes buy-only.
/// Items that merely use a *bought* ascended material can still be crafted. Ids verified vs /v2/items.
/// </summary>
public static class TimeGatedItems
{
    public static readonly IReadOnlySet<int> Ids = new HashSet<int>
    {
        43772, // Charged Quartz Crystal
        46740, // Spool of Silk Weaving Thread
        46742, // Lump of Mithrillium
        46744, // Glob of Elder Spirit Residue
        46745, // Spool of Thick Elonian Cord
    };
}
