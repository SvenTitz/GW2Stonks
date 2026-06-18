using GW2Stonks.Data;
using GW2Stonks.Models;
using Microsoft.EntityFrameworkCore;

namespace GW2Stonks.Services;

/// <summary>
/// Builds and caches the craft-cost graph from the DB, computes per-item craft profit for both
/// pricing modes, and produces craft-breakdown trees. The cached snapshot is rebuilt whenever the
/// prices or recipes have been re-synced.
/// </summary>
public sealed class ProfitService
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly ILogger<ProfitService> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Snapshot? _snapshot;

    public ProfitService(IDbContextFactory<AppDbContext> dbf, ILogger<ProfitService> log)
    {
        _dbf = dbf;
        _log = log;
    }

    public async Task<IReadOnlyList<ProfitRow>> GetRowsAsync(PricingMode mode, CancellationToken ct = default)
    {
        var snap = await EnsureCurrentAsync(ct);
        return mode == PricingMode.InstantBuy ? snap.InstantBuyRows : snap.BuyOrdersRows;
    }

    public async Task<CraftNode?> BuildTreeAsync(int itemId, PricingMode mode, CancellationToken ct = default)
    {
        var snap = await EnsureCurrentAsync(ct);
        var solver = mode == PricingMode.InstantBuy ? snap.InstantBuy : snap.BuyOrders;
        lock (snap.TreeLock)
        {
            // The root is the item we're evaluating *crafting*, so always expand its recipe
            // even if buying the finished item happens to be cheaper. Sub-components below it
            // then pick buy-vs-craft on their own.
            return BuildNode(itemId, 1, solver, snap, new HashSet<int>(), forceCraft: true);
        }
    }

    /// <summary>
    /// Explode a cart (itemId → quantity to craft) into a shopping list (all "buy" leaves summed,
    /// with batch-size rounding) and crafting steps (all "craft" nodes, deepest first), for one mode.
    /// </summary>
    public async Task<CraftPlan> BuildPlanAsync(
        IReadOnlyDictionary<int, int> cart, PricingMode mode, CancellationToken ct = default)
    {
        var snap = await EnsureCurrentAsync(ct);
        var solver = mode == PricingMode.InstantBuy ? snap.InstantBuy : snap.BuyOrders;

        var buyQty = new Dictionary<int, int>();
        var craftUnits = new Dictionary<int, int>();
        var craftRecipe = new Dictionary<int, int>();
        var craftDepth = new Dictionary<int, int>();

        lock (snap.TreeLock)
        {
            foreach (var (itemId, qty) in cart)
                if (qty > 0)
                    Explode(itemId, qty, 0, true, solver, snap, buyQty, craftUnits, craftRecipe, craftDepth, new HashSet<int>());
        }

        var plan = new CraftPlan();

        foreach (var (itemId, qty) in buyQty)
        {
            snap.Items.TryGetValue(itemId, out var meta);
            var unit = solver.BuyPrice(itemId);
            var source = VendorPrices.ByItemId.TryGetValue(itemId, out var v) && unit.HasValue && v <= unit.Value
                ? BuySource.Vendor
                : BuySource.TradingPost;

            plan.Shopping.Add(new ShoppingLine
            {
                ItemId = itemId,
                Name = meta?.Name ?? $"Item {itemId}",
                IconUrl = meta?.Icon,
                Quantity = qty,
                UnitPrice = Round(unit),
                TotalPrice = unit is null ? null : Round(unit.Value * qty),
                Source = source
            });
        }

        foreach (var (itemId, units) in craftUnits)
        {
            snap.Items.TryGetValue(itemId, out var meta);
            var rid = craftRecipe[itemId];
            var recipe = snap.RecipesByOutput[itemId].First(r => r.RecipeId == rid);
            var disciplines = snap.RecipeMeta.TryGetValue(rid, out var rm) ? rm.Disciplines : "";
            var outputCount = Math.Max(1, recipe.OutputCount);

            plan.Steps.Add(new CraftStep
            {
                ItemId = itemId,
                Name = meta?.Name ?? $"Item {itemId}",
                IconUrl = meta?.Icon,
                Quantity = units,
                Crafts = (int)Math.Ceiling(units / (double)outputCount),
                OutputCount = recipe.OutputCount,
                Disciplines = disciplines,
                IsFinal = cart.ContainsKey(itemId),
                Depth = craftDepth[itemId]
            });
        }

        AssignDisciplines(plan.Steps);

        plan.Shopping = plan.Shopping.OrderBy(s => s.Source).ThenByDescending(s => s.TotalPrice ?? 0).ToList();
        plan.Steps = plan.Steps.OrderByDescending(s => s.Depth).ThenBy(s => s.Discipline).ThenBy(s => s.Name).ToList();
        plan.TotalBuyCost = plan.Shopping.Sum(s => s.TotalPrice ?? 0);

        // Per-item cost / revenue / profit for the cart (the final products being crafted to sell).
        foreach (var (itemId, qty) in cart)
        {
            snap.Items.TryGetValue(itemId, out var meta);
            var res = solver.Resolve(itemId);
            var unitCost = res.Craft;                  // recursive craft cost per unit
            var unitSell = solver.SellRevenue(itemId); // net sell per unit (after tax)
            var cost = unitCost is null ? (int?)null : Round(unitCost.Value * qty);
            var revenue = unitSell is null ? (int?)null : Round(unitSell.Value * qty);
            var profit = (cost is null || revenue is null) ? (int?)null : revenue - cost;

            plan.CartLines.Add(new CartLine
            {
                ItemId = itemId,
                Name = meta?.Name ?? $"Item {itemId}",
                IconUrl = meta?.Icon,
                Quantity = qty,
                Cost = cost,
                Revenue = revenue,
                Profit = profit
            });
        }
        plan.CartLines = plan.CartLines.OrderBy(l => l.Name).ToList();
        plan.TotalRevenue = plan.CartLines.Sum(l => l.Revenue ?? 0);
        plan.TotalProfit = plan.CartLines.Sum(l => l.Profit ?? 0);
        return plan;
    }

    /// <summary>
    /// Assign each craft step a discipline, minimising the number of distinct disciplines used
    /// (so you switch crafting characters as little as possible): single-discipline steps fix the
    /// mandatory set, then flexible steps reuse an already-used discipline where possible.
    /// </summary>
    private static void AssignDisciplines(List<CraftStep> steps)
    {
        static List<string> Parse(string csv) =>
            csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var used = new HashSet<string>();
        var freq = new Dictionary<string, int>();
        foreach (var step in steps)
            foreach (var d in Parse(step.Disciplines))
                freq[d] = freq.GetValueOrDefault(d) + 1;

        foreach (var step in steps)
        {
            var cands = Parse(step.Disciplines);
            if (cands.Count == 1) { step.Discipline = cands[0]; used.Add(cands[0]); }
        }

        foreach (var step in steps)
        {
            if (!string.IsNullOrEmpty(step.Discipline)) continue;
            var cands = Parse(step.Disciplines);
            if (cands.Count == 0) { step.Discipline = ""; continue; }

            var reuse = cands.FirstOrDefault(used.Contains);
            if (reuse is not null)
            {
                step.Discipline = reuse;
            }
            else
            {
                var pick = cands.OrderByDescending(d => freq.GetValueOrDefault(d)).First();
                step.Discipline = pick;
                used.Add(pick);
            }
        }
    }

    // ── Snapshot management ─────────────────────────────────────────────────

    private async Task<Snapshot> EnsureCurrentAsync(CancellationToken ct)
    {
        var stamp = await ReadStampAsync(ct);
        var current = _snapshot;
        if (current is not null && current.Stamp == stamp) return current;

        await _gate.WaitAsync(ct);
        try
        {
            if (_snapshot is not null && _snapshot.Stamp == stamp) return _snapshot;
            _snapshot = await BuildSnapshotAsync(stamp, ct);
            return _snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(DateTime Prices, DateTime Recipes, DateTime Volume)> ReadStampAsync(CancellationToken ct)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var states = await db.SyncStates.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.LastSyncedUtc, ct);
        states.TryGetValue("prices", out var p);
        states.TryGetValue("recipes", out var r);
        states.TryGetValue("volume", out var v);
        return (p, r, v);
    }

    private async Task<Snapshot> BuildSnapshotAsync((DateTime, DateTime, DateTime) stamp, CancellationToken ct)
    {
        _log.LogInformation("Building profit snapshot…");
        await using var db = await _dbf.CreateDbContextAsync(ct);

        var prices = await db.Prices.AsNoTracking()
            .ToDictionaryAsync(p => p.ItemId, p => (p.BuyUnitPrice, p.SellUnitPrice), ct);

        var volumes = await db.ItemVolumes.AsNoTracking()
            .ToDictionaryAsync(v => v.ItemId, v => (v.SoldPerDay, v.SupplyNow), ct);

        var items = await db.Items.AsNoTracking()
            .Select(i => new ItemMeta(i.Id, i.Name, i.IconUrl, i.Type, i.Rarity))
            .ToDictionaryAsync(m => m.Id, ct);

        var recipeEntities = await db.Recipes.AsNoTracking().Include(r => r.Ingredients).ToListAsync(ct);

        var recipesByOutput = new Dictionary<int, List<CraftCostSolver.RecipeInfo>>();
        var recipeMeta = new Dictionary<int, RecipeMeta>();
        foreach (var r in recipeEntities)
        {
            var info = new CraftCostSolver.RecipeInfo(
                r.Id, r.OutputItemCount,
                r.Ingredients.Select(g => (g.ItemId, g.Count)).ToList());

            if (!recipesByOutput.TryGetValue(r.OutputItemId, out var list))
                recipesByOutput[r.OutputItemId] = list = new List<CraftCostSolver.RecipeInfo>();
            list.Add(info);

            recipeMeta[r.Id] = new RecipeMeta(r.Disciplines, r.MinRating, r.OutputItemCount);
        }

        var instantBuy = new CraftCostSolver(prices, recipesByOutput, VendorPrices.ByItemId, TimeGatedItems.Ids, PricingMode.InstantBuy);
        var buyOrders = new CraftCostSolver(prices, recipesByOutput, VendorPrices.ByItemId, TimeGatedItems.Ids, PricingMode.BuyOrders);

        var snap = new Snapshot
        {
            Stamp = stamp,
            Prices = prices,
            Items = items,
            RecipesByOutput = recipesByOutput,
            RecipeMeta = recipeMeta,
            Volumes = volumes,
            InstantBuy = instantBuy,
            BuyOrders = buyOrders
        };

        snap.InstantBuyRows = BuildRows(instantBuy, snap);
        snap.BuyOrdersRows = BuildRows(buyOrders, snap);

        _log.LogInformation("Profit snapshot built: {I} / {B} profitable-eligible rows (instant-buy/buy-orders)",
            snap.InstantBuyRows.Count, snap.BuyOrdersRows.Count);
        return snap;
    }

    private static List<ProfitRow> BuildRows(CraftCostSolver solver, Snapshot snap)
    {
        var rows = new List<ProfitRow>();
        foreach (var outputId in snap.RecipesByOutput.Keys)
        {
            if (!snap.Items.TryGetValue(outputId, out var meta)) continue;

            var sell = solver.SellRevenue(outputId);
            if (sell is null) continue; // not sellable on the TP

            var res = solver.Resolve(outputId);
            if (res.Craft is null) continue; // can't fully price the craft

            var craft = res.Craft.Value;
            var profit = sell.Value - craft;
            var meta2 = res.RecipeId is int rid && snap.RecipeMeta.TryGetValue(rid, out var rm)
                ? rm : new RecipeMeta("", 0, 1);

            int? soldPerDay = null;
            double? sellThrough = null;
            if (snap.Volumes.TryGetValue(outputId, out var vol))
            {
                soldPerDay = vol.SoldPerDay;
                sellThrough = vol.SoldPerDay > 0 ? (double)vol.SupplyNow / vol.SoldPerDay : null;
            }

            // Each relist costs the 5% listing fee on the ask (= sell / 0.85). How many times you
            // could eat that fee before the profit is gone.
            var listingFee = sell.Value * (0.05m / 0.85m);
            int? relists = listingFee > 0 ? Math.Max(0, (int)Math.Floor(profit / listingFee)) : null;

            rows.Add(new ProfitRow
            {
                Id = outputId,
                Name = meta.Name,
                IconUrl = meta.Icon,
                Type = meta.Type,
                Rarity = meta.Rarity,
                Disciplines = meta2.Disciplines,
                MinRating = meta2.MinRating,
                OutputCount = meta2.OutputCount,
                BuyPrice = Round(res.Buy),
                CraftCost = Round(craft),
                NetSell = Round(sell),
                Profit = Round(profit),
                Margin = craft > 0 ? (double)(profit / craft) * 100 : null,
                Relists = relists,
                CraftCheaperThanBuy = res.Buy is not null && craft < res.Buy.Value,
                SoldPerDay = soldPerDay,
                SellThroughDays = sellThrough
            });
        }
        return rows;
    }

    private CraftNode BuildNode(int itemId, int count, CraftCostSolver solver, Snapshot snap, HashSet<int> path,
        bool forceCraft = false)
    {
        var res = solver.Resolve(itemId);
        snap.Items.TryGetValue(itemId, out var meta);

        var willCraft = forceCraft || res.Source == CraftCostSolver.Source.Craft;

        var node = new CraftNode
        {
            ItemId = itemId,
            Count = count,
            Name = meta?.Name ?? $"Item {itemId}",
            IconUrl = meta?.Icon,
            UnitBest = Round(res.Best),
            UnitBuy = Round(res.Buy),
            UnitCraft = Round(res.Craft),
            Decision = willCraft && res.Craft is not null ? "Craft"
                : res.Source == CraftCostSolver.Source.Buy ? "Buy"
                : "—"
        };

        if (willCraft && res.Craft is not null && res.RecipeId is int rid &&
            !path.Contains(itemId) &&
            snap.RecipesByOutput.TryGetValue(itemId, out var recipes))
        {
            var recipe = recipes.FirstOrDefault(r => r.RecipeId == rid);
            if (recipe is not null)
            {
                node.OutputCount = recipe.OutputCount;
                path.Add(itemId);
                foreach (var (ingId, c) in recipe.Ingredients)
                    node.Children.Add(BuildNode(ingId, c, solver, snap, path));
                path.Remove(itemId);
            }
        }

        return node;
    }

    private static void Explode(
        int itemId, int qty, int depth, bool forceCraft,
        CraftCostSolver solver, Snapshot snap,
        Dictionary<int, int> buyQty, Dictionary<int, int> craftUnits,
        Dictionary<int, int> craftRecipe, Dictionary<int, int> craftDepth, HashSet<int> path)
    {
        var res = solver.Resolve(itemId);
        var willCraft = (forceCraft || res.Source == CraftCostSolver.Source.Craft)
            && res.Craft is not null
            && res.RecipeId is int
            && !path.Contains(itemId)
            && snap.RecipesByOutput.ContainsKey(itemId);

        if (!willCraft)
        {
            buyQty[itemId] = buyQty.GetValueOrDefault(itemId) + qty;
            return;
        }

        var rid = res.RecipeId!.Value;
        var recipe = snap.RecipesByOutput[itemId].First(r => r.RecipeId == rid);
        var outputCount = Math.Max(1, recipe.OutputCount);
        var crafts = (int)Math.Ceiling(qty / (double)outputCount);

        craftUnits[itemId] = craftUnits.GetValueOrDefault(itemId) + crafts * outputCount;
        craftRecipe[itemId] = rid;
        craftDepth[itemId] = Math.Max(craftDepth.GetValueOrDefault(itemId), depth);

        path.Add(itemId);
        foreach (var (ingId, count) in recipe.Ingredients)
            Explode(ingId, crafts * count, depth + 1, false, solver, snap, buyQty, craftUnits, craftRecipe, craftDepth, path);
        path.Remove(itemId);
    }

    private static int? Round(decimal? value) =>
        value is null ? null : (int)Math.Round(value.Value, MidpointRounding.AwayFromZero);

    private sealed record RecipeMeta(string Disciplines, int MinRating, int OutputCount);

    private sealed class Snapshot
    {
        public (DateTime Prices, DateTime Recipes, DateTime Volume) Stamp { get; set; }
        public required IReadOnlyDictionary<int, (int Buy, int Sell)> Prices { get; init; }
        public required IReadOnlyDictionary<int, ItemMeta> Items { get; init; }
        public required IReadOnlyDictionary<int, List<CraftCostSolver.RecipeInfo>> RecipesByOutput { get; init; }
        public required IReadOnlyDictionary<int, RecipeMeta> RecipeMeta { get; init; }
        public required IReadOnlyDictionary<int, (int SoldPerDay, int SupplyNow)> Volumes { get; init; }
        public required CraftCostSolver InstantBuy { get; init; }
        public required CraftCostSolver BuyOrders { get; init; }
        public List<ProfitRow> InstantBuyRows { get; set; } = new();
        public List<ProfitRow> BuyOrdersRows { get; set; } = new();
        public object TreeLock { get; } = new();
    }
}
