namespace GW2Stonks.Models;

/// <summary>An item in the to-be-crafted cart.</summary>
public sealed class CartItem
{
    public int ItemId { get; set; }
    public string Name { get; set; } = "";
    public string? IconUrl { get; set; }
    public int Quantity { get; set; }
}
