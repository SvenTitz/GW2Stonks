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

        var instantBuy = new CraftCostSolver(prices, recipesByOutput, VendorPrices.ByItemId, PricingMode.InstantBuy);
        var buyOrders = new CraftCostSolver(prices, recipesByOutput, VendorPrices.ByItemId, PricingMode.BuyOrders);

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
