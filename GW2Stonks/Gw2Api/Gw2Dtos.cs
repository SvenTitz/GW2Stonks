namespace GW2Stonks.Gw2Api;

// DTOs mirror the GW2 API v2 JSON. Snake_case property names (item_id, unit_price, …)
// are mapped via JsonNamingPolicy.SnakeCaseLower configured in Gw2ApiClient.

public sealed class Gw2ItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Icon { get; set; }
    public string Type { get; set; } = "";
    public string Rarity { get; set; } = "";
    public int Level { get; set; }
    public int VendorValue { get; set; }
    public List<string> Flags { get; set; } = new();
}

public sealed class Gw2PriceDto
{
    public int Id { get; set; }
    public bool Whitelisted { get; set; }
    public Gw2PricePointDto? Buys { get; set; }
    public Gw2PricePointDto? Sells { get; set; }
}

public sealed class Gw2PricePointDto
{
    public int Quantity { get; set; }
    public int UnitPrice { get; set; }
}

public sealed class Gw2RecipeDto
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public int OutputItemId { get; set; }
    public int OutputItemCount { get; set; }
    public int MinRating { get; set; }
    public int TimeToCraftMs { get; set; }
    public List<string> Disciplines { get; set; } = new();
    public List<string> Flags { get; set; } = new();
    public List<Gw2IngredientDto> Ingredients { get; set; } = new();
}

public sealed class Gw2IngredientDto
{
    public int ItemId { get; set; }
    public int Count { get; set; }
}
