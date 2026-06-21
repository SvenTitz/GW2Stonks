namespace GW2Stonks.Data.Entities;

/// <summary>
/// A persisted "to craft" cart line, so the cart survives app restarts. Name/icon are stored
/// denormalised (as the cart was built) so loading needs no join and tolerates uncatalogued items.
/// </summary>
public class CartEntry
{
    public int ItemId { get; set; }
    public string Name { get; set; } = "";
    public string? IconUrl { get; set; }
    public int Quantity { get; set; }
}
