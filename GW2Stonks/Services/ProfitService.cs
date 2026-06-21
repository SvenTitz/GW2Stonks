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

    /// <summary>Items usable in the "requires ingredient" filter (anything used as an ingredient), by name.</summary>
    public async Task<IReadOnlyList<ItemOption>> GetIngredientOptionsAsync(CancellationToken ct = default)
    {
        var snap = await EnsureCurrentAsync(ct);
        return snap.IngredientOptions;
    }

    /// <summary>
    /// Every item whose recursive craft tree contains <paramref name="ingredientId"/> (BFS over the
    /// reverse-ingredient graph). The ingredient itself is not included.
    /// </summary>
    public async Task<IReadOnlySet<int>> GetItemsRequiringAsync(int ingredientId, CancellationToken ct = default)
    {
        var snap = await EnsureCurrentAsync(ct);
        var result = new HashSet<int>();
        var seen = new HashSet<int> { ingredientId };
        var queue = new Queue<int>();
        queue.Enqueue(ingredientId);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (!snap.DirectUsers.TryGetValue(cur, out var users)) continue;
            foreach (var u in users)
                if (seen.Add(u)) { result.Add(u); queue.Enqueue(u); }
        }
        return result;
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
        IReadOnlyDictionary<int, int> cart, PricingMode mode,
        IReadOnlyDictionary<int, int>? owned = null, CancellationToken ct = default)
    {
        var snap = await EnsureCurrentAsync(ct);
        var solver = mode == PricingMode.InstantBuy ? snap.InstantBuy : snap.BuyOrders;

        var buyQty = new Dictionary<int, int>();
        var craftUnits = new Dictionary<int, int>();
        var craftRecipe = new Dictionary<int, int>();
        var craftDepth = new Dictionary<int, int>();

        // Mutable pool of owned stock drawn down during explosion (cart items themselves are
        // always crafted in full; only their ingredients/intermediates are covered by stock).
        var available = owned is null ? new Dictionary<int, int>() : new Dictionary<int, int>(owned);
        var ownedUsed = new Dictionary<int, int>();

        lock (snap.TreeLock)
        {
            foreach (var (itemId, qty) in cart)
                if (qty > 0)
                    Explode(itemId, qty, 0, true, solver, snap, buyQty, craftUnits, craftRecipe, craftDepth, available, ownedUsed, new HashSet<int>());
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

        // Who consumes whom among crafted items — drives discipline assignment + ordering so a
        // subcomponent is crafted at the station that needs it, before its consumer.
        var consumers = new Dictionary<int, List<int>>();
        foreach (var (itemId, _) in craftUnits)
        {
            var recipe = snap.RecipesByOutput[itemId].First(r => r.RecipeId == craftRecipe[itemId]);
            foreach (var (ingId, _) in recipe.Ingredients)
                if (craftUnits.ContainsKey(ingId))
                {
                    if (!consumers.TryGetValue(ingId, out var list))
                        consumers[ingId] = list = new List<int>();
                    list.Add(itemId);
                }
        }

        AssignDisciplines(plan.Steps, consumers);

        plan.Shopping = plan.Shopping.OrderBy(s => s.Source).ThenByDescending(s => s.TotalPrice ?? 0).ToList();
        plan.Steps = OrderSteps(plan.Steps, consumers);
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

        // Itemised owned stock that was applied: each used unit saved its cheapest acquisition cost.
        foreach (var (itemId, used) in ownedUsed)
        {
            snap.Items.TryGetValue(itemId, out var meta);
            var best = solver.Resolve(itemId).Best;
            plan.OwnedUsed.Add(new OwnedUsedLine
            {
                ItemId = itemId,
                Name = meta?.Name ?? $"Item {itemId}",
                IconUrl = meta?.Icon,
                Quantity = used,
                Value = best is null ? null : Round(best.Value * used)
            });
        }
        plan.OwnedUsed = plan.OwnedUsed.OrderByDescending(l => l.Value ?? 0).ThenBy(l => l.Name).ToList();
        plan.OwnedTypesApplied = plan.OwnedUsed.Count;
        plan.OwnedSavings = plan.OwnedUsed.Sum(l => l.Value ?? 0);
        return plan;
    }

    private static List<string> ParseDisciplines(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>
    /// Assign each craft step a discipline so a subcomponent is crafted at the station that needs it:
    /// single-discipline steps are fixed; a flexible step (craftable at several disciplines, e.g. an
    /// insignia) follows a consuming step to that station when possible. Processed finished-product
    /// first so consumers are decided before their subcomponents; "first occurrence" = deepest consumer.
    /// </summary>
    private static void AssignDisciplines(List<CraftStep> steps, IReadOnlyDictionary<int, List<int>> consumers)
    {
        var stepById = steps.ToDictionary(s => s.ItemId);

        var freq = new Dictionary<string, int>();
        foreach (var step in steps)
            foreach (var d in ParseDisciplines(step.Disciplines))
                freq[d] = freq.GetValueOrDefault(d) + 1;

        // Fixed: items craftable at only one discipline.
        foreach (var step in steps)
        {
            var cands = ParseDisciplines(step.Disciplines);
            if (cands.Count == 1) step.Discipline = cands[0];
        }

        // Flexible: finished products first (low depth) so a consumer's discipline is known before
        // the subcomponent it uses, letting the subcomponent follow it to the same station.
        foreach (var step in steps.Where(s => string.IsNullOrEmpty(s.Discipline)).OrderBy(s => s.Depth).ToList())
        {
            var cands = ParseDisciplines(step.Disciplines);
            if (cands.Count == 0) { step.Discipline = ""; continue; }

            string? pick = null;
            if (consumers.TryGetValue(step.ItemId, out var cons))
                pick = cons
                    .Select(id => stepById.GetValueOrDefault(id))
                    .Where(c => c is not null && !string.IsNullOrEmpty(c!.Discipline) && cands.Contains(c.Discipline))
                    .OrderByDescending(c => c!.Depth).ThenBy(c => c!.Name)
                    .Select(c => c!.Discipline)
                    .FirstOrDefault();

            // No consuming station can make it: keep switching low — reuse a discipline already in
            // play, else the most common candidate.
            pick ??= cands.FirstOrDefault(c => steps.Any(o => o.Discipline == c))
                     ?? cands.OrderByDescending(c => freq.GetValueOrDefault(c)).First();

            step.Discipline = pick;
        }
    }

    /// <summary>
    /// Order craft steps so dependencies come first: within a station, deepest sub-components before
    /// their consumers; across stations, a station whose output feeds another is listed first
    /// (topological sort, with a deepest-first fallback if disciplines form a cycle).
    /// </summary>
    private static List<CraftStep> OrderSteps(List<CraftStep> steps, IReadOnlyDictionary<int, List<int>> consumers)
    {
        var stepById = steps.ToDictionary(s => s.ItemId);
        var disciplines = steps.Select(s => s.Discipline).Distinct().ToList();

        // Station edges: disc(subcomponent) -> disc(consumer) when they differ.
        var outs = disciplines.ToDictionary(d => d, _ => new HashSet<string>());
        var indeg = disciplines.ToDictionary(d => d, _ => 0);
        foreach (var (ingId, cons) in consumers)
        {
            if (!stepById.TryGetValue(ingId, out var ing)) continue;
            foreach (var cId in cons)
            {
                if (!stepById.TryGetValue(cId, out var c) || ing.Discipline == c.Discipline) continue;
                if (outs[ing.Discipline].Add(c.Discipline)) indeg[c.Discipline]++;
            }
        }

        var maxDepth = disciplines.ToDictionary(d => d, d => steps.Where(s => s.Discipline == d).Max(s => s.Depth));

        // Kahn topological sort; among ready stations the one making the deepest items goes first.
        var order = new List<string>();
        var ready = disciplines.Where(d => indeg[d] == 0).ToList();
        while (ready.Count > 0)
        {
            var d = ready.OrderByDescending(x => maxDepth[x]).ThenBy(x => x).First();
            ready.Remove(d);
            order.Add(d);
            foreach (var b in outs[d])
                if (--indeg[b] == 0) ready.Add(b);
        }
        order.AddRange(disciplines.Except(order).OrderByDescending(d => maxDepth[d]).ThenBy(d => d));

        var orderIndex = order.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);

        return steps
            .OrderBy(s => orderIndex.GetValueOrDefault(s.Discipline, int.MaxValue))
            .ThenByDescending(s => s.Depth)
            .ThenBy(s => s.Name)
            .ToList();
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
        // Reverse index: ingredient id -> output items that directly use it. BFS over this finds
        // every item whose recursive craft tree contains a given ingredient.
        var directUsers = new Dictionary<int, List<int>>();
        foreach (var r in recipeEntities)
        {
            var info = new CraftCostSolver.RecipeInfo(
                r.Id, r.OutputItemCount,
                r.Ingredients.Select(g => (g.ItemId, g.Count)).ToList());

            if (!recipesByOutput.TryGetValue(r.OutputItemId, out var list))
                recipesByOutput[r.OutputItemId] = list = new List<CraftCostSolver.RecipeInfo>();
            list.Add(info);

            recipeMeta[r.Id] = new RecipeMeta(r.Disciplines, r.MinRating, r.OutputItemCount);

            foreach (var ing in r.Ingredients)
            {
                if (!directUsers.TryGetValue(ing.ItemId, out var users))
                    directUsers[ing.ItemId] = users = new List<int>();
                users.Add(r.OutputItemId);
            }
        }

        // Items selectable in the "requires ingredient" filter: anything used as an ingredient.
        var ingredientOptions = directUsers.Keys
            .Where(id => items.TryGetValue(id, out var m) && !string.IsNullOrEmpty(m.Name))
            .Select(id => new ItemOption(id, items[id].Name, items[id].Icon))
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

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
            BuyOrders = buyOrders,
            DirectUsers = directUsers,
            IngredientOptions = ingredientOptions
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
        Dictionary<int, int> craftRecipe, Dictionary<int, int> craftDepth,
        Dictionary<int, int> available, Dictionary<int, int> ownedUsed, HashSet<int> path)
    {
        // Draw down owned stock for ingredients/intermediates (never for the forced cart roots).
        if (!forceCraft && available.TryGetValue(itemId, out var have) && have > 0)
        {
            var use = Math.Min(have, qty);
            available[itemId] = have - use;
            ownedUsed[itemId] = ownedUsed.GetValueOrDefault(itemId) + use;
            qty -= use;
            if (qty <= 0) return;
        }

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
            Explode(ingId, crafts * count, depth + 1, false, solver, snap, buyQty, craftUnits, craftRecipe, craftDepth, available, ownedUsed, path);
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
        public required IReadOnlyDictionary<int, List<int>> DirectUsers { get; init; }
        public required IReadOnlyList<ItemOption> IngredientOptions { get; init; }
        public List<ProfitRow> InstantBuyRows { get; set; } = new();
        public List<ProfitRow> BuyOrdersRows { get; set; } = new();
        public object TreeLock { get; } = new();
    }
}
