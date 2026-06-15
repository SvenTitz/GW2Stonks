namespace GW2Stonks.Gw2Api;

/// <summary>Full order book for an item from <c>/v2/commerce/listings</c>.</summary>
public sealed class Gw2ListingsDto
{
    public int Id { get; set; }

    /// <summary>Buy orders (bids), highest price first.</summary>
    public List<Gw2ListingDto> Buys { get; set; } = new();

    /// <summary>Sell listings (asks), lowest price first.</summary>
    public List<Gw2ListingDto> Sells { get; set; } = new();
}

/// <summary>One price level in the order book.</summary>
public sealed class Gw2ListingDto
{
    /// <summary>Number of individual listings at this price.</summary>
    public int Listings { get; set; }

    public int UnitPrice { get; set; }

    /// <summary>Total quantity at this price.</summary>
    public int Quantity { get; set; }
}
