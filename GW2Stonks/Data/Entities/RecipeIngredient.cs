namespace GW2Stonks.Data.Entities;

/// <summary>A single ingredient line of a <see cref="Recipe"/>.</summary>
public class RecipeIngredient
{
    /// <summary>Locally generated surrogate key.</summary>
    public int Id { get; set; }

    public int RecipeId { get; set; }

    /// <summary>The ingredient item required.</summary>
    public int ItemId { get; set; }

    /// <summary>How many of the ingredient a single craft consumes.</summary>
    public int Count { get; set; }

    public Recipe Recipe { get; set; } = null!;

    public Item? Item { get; set; }
}
