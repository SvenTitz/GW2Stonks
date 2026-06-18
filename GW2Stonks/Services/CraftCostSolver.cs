namespace GW2Stonks.Services;

/// <summary>
/// Computes, for a single pricing mode, the cheapest way to obtain each item:
/// buy it, or craft it from ingredients (recursively choosing the cheaper option per ingredient).
/// Results are memoised; cycles fall back to buying. All costs are in copper (decimal for precision).
/// </summary>
public sealed class CraftCostSolver
{
    public sealed record RecipeInfo(int RecipeId, int OutputCount, IReadOnlyList<(int ItemId, int Count)> Ingredients);

    public enum Source { None, Buy, Craft }

    public sealed record CostResult(decimal? Best, decimal? Buy, decimal? Craft, Source Source, int? RecipeId);

    private const decimal TaxMultiplier = 0.85m; // 15% trading-post tax on sales

    private readonly IReadOnlyDictionary<int, (int Buy, int Sell)> _prices;
    private readonly IReadOnlyDictionary<int, List<RecipeInfo>> _recipesByOutput;
    private readonly IReadOnlyDictionary<int, int> _vendor;
    private readonly IReadOnlySet<int> _timeGated;
    private readonly PricingMode _mode;

    private readonly Dictionary<int, CostResult> _memo = new();
    private readonly HashSet<int> _visiting = new();

    public CraftCostSolver(
        IReadOnlyDictionary<int, (int Buy, int Sell)> prices,
        IReadOnlyDictionary<int, List<RecipeInfo>> recipesByOutput,
        IReadOnlyDictionary<int, int> vendor,
        IReadOnlySet<int> timeGated,
        PricingMode mode)
    {
        _prices = prices;
        _recipesByOutput = recipesByOutput;
        _vendor = vendor;
        _timeGated = timeGated;
        _mode = mode;
    }

    public PricingMode Mode => _mode;

    /// <summary>Cheapest direct purchase price (TP in this mode vs vendor), or null if not directly obtainable.</summary>
    public decimal? BuyPrice(int itemId)
    {
        decimal? tp = null;
        if (_prices.TryGetValue(itemId, out var p))
        {
            // Instant buy pays the lowest sell-listing (ask); buy orders pay the highest bid.
            var unit = _mode == PricingMode.InstantBuy ? p.Sell : p.Buy;
            if (unit > 0) tp = unit;
        }

        decimal? vendor = _vendor.TryGetValue(itemId, out var v) ? v : null;
        return MinNullable(tp, vendor);
    }

    /// <summary>
    /// What you receive selling the item. Always assumes listing at the sell-listing (ask) price
    /// and waiting (nets more than an instant sell), minus the 15% tax. Null if it isn't sellable.
    /// </summary>
    public decimal? SellRevenue(int itemId)
    {
        if (!_prices.TryGetValue(itemId, out var p)) return null;
        return p.Sell > 0 ? p.Sell * TaxMultiplier : null;
    }

    /// <summary>Resolve the best acquisition cost (and decision) for an item.</summary>
    public CostResult Resolve(int itemId)
    {
        if (_memo.TryGetValue(itemId, out var cached)) return cached;

        if (_visiting.Contains(itemId))
        {
            // Cycle: only buying is viable here (don't memoise — it's path-dependent).
            var buyOnly = BuyPrice(itemId);
            return new CostResult(buyOnly, buyOnly, null, buyOnly is null ? Source.None : Source.Buy, null);
        }

        _visiting.Add(itemId);
        var result = Compute(itemId);
        _visiting.Remove(itemId);

        _memo[itemId] = result;
        return result;
    }

    private CostResult Compute(int itemId)
    {
        var buy = BuyPrice(itemId);

        decimal? bestCraft = null;
        int? bestRecipe = null;

        // Time-gated daily materials are never crafted (always bought), and a recipe that directly
        // requires one is disallowed — so its output also becomes buy-only.
        if (!_timeGated.Contains(itemId) && _recipesByOutput.TryGetValue(itemId, out var recipes))
        {
            foreach (var r in recipes)
            {
                if (r.OutputCount <= 0) continue;
                if (r.Ingredients.Any(g => _timeGated.Contains(g.ItemId))) continue;

                decimal total = 0;
                bool feasible = true;
                foreach (var (ingId, count) in r.Ingredients)
                {
                    var ing = Resolve(ingId).Best;
                    if (ing is null) { feasible = false; break; }
                    total += ing.Value * count;
                }

                if (!feasible) continue;

                var perUnit = total / r.OutputCount;
                if (bestCraft is null || perUnit < bestCraft)
                {
                    bestCraft = perUnit;
                    bestRecipe = r.RecipeId;
                }
            }
        }

        var best = MinNullable(buy, bestCraft);
        var source = best is null ? Source.None : (best == buy ? Source.Buy : Source.Craft);
        // RecipeId always carries the cheapest craft recipe (if any) so callers can show craft
        // details even when buying is the cheaper option overall.
        return new CostResult(best, buy, bestCraft, source, bestRecipe);
    }

    private static decimal? MinNullable(decimal? a, decimal? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return Math.Min(a.Value, b.Value);
    }
}
