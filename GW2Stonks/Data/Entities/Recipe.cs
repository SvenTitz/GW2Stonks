namespace GW2Stonks.Data.Entities;

/// <summary>A crafting recipe, as returned by <c>/v2/recipes</c>.</summary>
public class Recipe
{
    /// <summary>GW2 recipe id (assigned by the API, not generated locally).</summary>
    public int Id { get; set; }

    /// <summary>Recipe type, e.g. "RefinementEctoplasm", "Insignia".</summary>
    public string Type { get; set; } = "";

    /// <summary>Item produced by this recipe.</summary>
    public int OutputItemId { get; set; }

    /// <summary>How many of the output item a single craft yields.</summary>
    public int OutputItemCount { get; set; }

    /// <summary>Minimum crafting discipline rating required.</summary>
    public int MinRating { get; set; }

    public int TimeToCraftMs { get; set; }

    /// <summary>Comma-separated disciplines that can craft this (e.g. "Armorsmith,Chef").</summary>
    public string Disciplines { get; set; } = "";

    /// <summary>Comma-separated recipe flags (e.g. "AutoLearned,LearnedFromItem").</summary>
    public string Flags { get; set; } = "";

    public Item? OutputItem { get; set; }

    public ICollection<RecipeIngredient> Ingredients { get; set; } = new List<RecipeIngredient>();
}
