namespace GW2Stonks.Gw2Api;

// DTOs for the authenticated GW2 account endpoints. Only the fields the planner needs are mapped.
// Property names map to snake_case JSON via the policy configured in Gw2ApiClient.

/// <summary>Result of /v2/tokeninfo — used to validate a key and show its scopes.</summary>
public sealed class Gw2TokenInfoDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Permissions { get; set; } = new();
}

/// <summary>
/// A storage/inventory slot holding a stack of an item. Covers /v2/account/materials rows and
/// the (nullable) slots in /v2/account/bank, /v2/account/inventory and character bags.
/// </summary>
public sealed class Gw2ItemSlotDto
{
    public int Id { get; set; }
    public int Count { get; set; }
}

/// <summary>A single character's bags from /v2/characters/{name}/inventory.</summary>
public sealed class Gw2CharacterInventoryDto
{
    public List<Gw2BagDto?> Bags { get; set; } = new();
}

public sealed class Gw2BagDto
{
    public int Id { get; set; }
    public int Size { get; set; }
    public List<Gw2ItemSlotDto?> Inventory { get; set; } = new();
}

/// <summary>One current trading-post transaction (a buy order or sell listing) from /v2/commerce/transactions/current/*.</summary>
public sealed class Gw2TransactionDto
{
    public long Id { get; set; }
    public int ItemId { get; set; }

    /// <summary>Unit price in copper.</summary>
    public int Price { get; set; }

    /// <summary>Units remaining in this listing/order.</summary>
    public int Quantity { get; set; }

    public string? Created { get; set; }
}
