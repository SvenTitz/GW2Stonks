namespace GW2Stonks.Data.Entities;

/// <summary>
/// Trading-post prices for an item, from <c>/v2/commerce/prices</c>. All values are in copper.
/// </summary>
public class Price
{
    /// <summary>Item these prices belong to (also the primary key).</summary>
    public int ItemId { get; set; }

    /// <summary>Highest standing buy order — what you receive if you sell instantly.</summary>
    public int BuyUnitPrice { get; set; }

    /// <summary>Quantity demanded at the highest buy order.</summary>
    public int BuyQuantity { get; set; }

    /// <summary>Lowest standing sell listing — what you pay if you buy instantly.</summary>
    public int SellUnitPrice { get; set; }

    /// <summary>Quantity available at the lowest sell listing.</summary>
    public int SellQuantity { get; set; }

    /// <summary>When these prices were last fetched from the API.</summary>
    public DateTime UpdatedUtc { get; set; }

    public Item? Item { get; set; }
}
