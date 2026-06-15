namespace GW2Stonks.Services;

/// <summary>
/// Fixed copper costs for crafting materials bought from NPC vendors (not the trading post).
/// This is a seed list — extend it as needed. The solver always takes min(vendor, trading-post),
/// so an entry here only ever adds a price floor; it never hides a cheaper TP option.
/// Item ids verified against /v2/items.
/// </summary>
public static class VendorPrices
{
    public static readonly IReadOnlyDictionary<int, int> ByItemId = new Dictionary<int, int>
    {
        // Metalworking lumps
        [19704] = 8,    // Lump of Tin
        [19750] = 16,   // Lump of Coal
        [19924] = 48,   // Lump of Primordium

        // Thread (tailor / leatherworker)
        [19792] = 8,    // Spool of Jute Thread
        [19789] = 16,   // Spool of Wool Thread
        [19794] = 24,   // Spool of Cotton Thread
        [19793] = 32,   // Spool of Linen Thread
        [19791] = 48,   // Spool of Silk Thread
        [19790] = 64,   // Spool of Gossamer Thread

        // Reagent (used across many disciplines)
        [46747] = 150,  // Thermocatalytic Reagent

        // Cooking staples (Cook's ingredients vendor)
        [12136] = 8,    // Bag of Flour
        [12151] = 8,    // Packet of Baking Powder
        [12157] = 8,    // Jar of Vinegar
        [12158] = 8,    // Jar of Vegetable Oil
    };
}
