namespace GW2Stonks.Services;

/// <summary>
/// How crafting materials are acquired. The crafted output is always assumed to be sold at the
/// sell-listing (the higher "list it and wait" price) minus the 15% trading-post tax, regardless
/// of mode — only the material buy cost changes.
/// </summary>
public enum PricingMode
{
    /// <summary>Buy materials immediately at the lowest sell-listing (ask) price.</summary>
    InstantBuy,

    /// <summary>Acquire materials by placing buy orders at the highest buy-order (bid) price.</summary>
    BuyOrders
}
