using GW2Stonks.Data;
using GW2Stonks.Gw2Api;
using GW2Stonks.Models;
using Microsoft.EntityFrameworkCore;

namespace GW2Stonks.Services;

/// <summary>
/// Prices the static <see cref="ProvisionerData"/> against the live trading post. For each Faction
/// Provisioner tab it finds the cheapest item to buy 7× — costed from the <b>average of the 7
/// cheapest sell listings</b> (walking the order book, since you buy seven). Daily-rotation
/// materials are priced from the cached single sell listing.
/// </summary>
public sealed class ProvisionerService
{
    private const int WeeklyTrades = 7;

    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly Gw2ApiClient _api;
    private readonly ILogger<ProvisionerService> _log;

    public ProvisionerService(IDbContextFactory<AppDbContext> dbf, Gw2ApiClient api, ILogger<ProvisionerService> log)
    {
        _dbf = dbf;
        _api = api;
        _log = log;
    }

    public async Task<ProvisionerView> GetAsync(CancellationToken ct = default)
    {
        var names = ProvisionerData.Vendors
            .SelectMany(v => v.Tabs.SelectMany(t => t.Items))
            .Concat(ProvisionerData.DailyRotationMaterials.Select(o => o.ItemName))
            .Distinct().ToList();

        await using var db = await _dbf.CreateDbContextAsync(ct);
        var matches = await db.Items.AsNoTracking()
            .Where(i => names.Contains(i.Name))
            .Select(i => new { i.Id, i.Name, i.IconUrl, Sell = i.Price != null ? (int?)i.Price.SellUnitPrice : null })
            .ToListAsync(ct);

        var byName = matches
            .GroupBy(m => m.Name)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.Sell.HasValue).First());

        // Live order books for the faction items, so we can average the 7 cheapest listings.
        var factionIds = ProvisionerData.Vendors
            .SelectMany(v => v.Tabs.SelectMany(t => t.Items))
            .Select(n => byName.TryGetValue(n, out var m) ? m.Id : (int?)null)
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();

        var sellsById = new Dictionary<int, List<Gw2ListingDto>>();
        try
        {
            foreach (var chunk in factionIds.Chunk(200))
                foreach (var b in await _api.GetListingsAsync(chunk, ct))
                    sellsById[b.Id] = b.Sells;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not fetch order books; falling back to cached sell prices");
        }

        PricedRow PriceFaction(string name)
        {
            byName.TryGetValue(name, out var m);
            int? avg = null, weekly = null;
            if (m is not null)
            {
                if (sellsById.TryGetValue(m.Id, out var sells))
                    (avg, weekly) = AverageOfCheapest(sells, WeeklyTrades);
                if (avg is null && m.Sell is int s && s > 0) // fallback: cached single price
                    (avg, weekly) = (s, s * WeeklyTrades);
            }
            return new PricedRow
            {
                ItemName = name, ItemId = m?.Id, IconUrl = m?.IconUrl, Quantity = 1,
                UnitPrice = avg, CostPerToken = avg, Weekly7Cost = weekly
            };
        }

        var vendors = new List<VendorView>();
        foreach (var v in ProvisionerData.Vendors)
        {
            var tabs = new List<TabBest>();
            foreach (var tab in v.Tabs)
            {
                var options = tab.Items.Select(PriceFaction)
                    .OrderBy(r => r.Weekly7Cost ?? int.MaxValue).ThenBy(r => r.ItemName)
                    .ToList();
                var cheapest = options.FirstOrDefault(o => o.Weekly7Cost is not null);
                tabs.Add(new TabBest
                {
                    Tab = tab.Name,
                    ItemName = cheapest?.ItemName,
                    ItemId = cheapest?.ItemId,
                    IconUrl = cheapest?.IconUrl,
                    UnitPrice = cheapest?.UnitPrice,
                    WeeklyCost = cheapest?.Weekly7Cost,
                    Options = options
                });
            }

            vendors.Add(new VendorView
            {
                Name = v.Name, Zone = v.Zone, Waypoint = v.Waypoint, WaypointChatLink = v.WaypointChatLink,
                Limit = v.Limit, Tabs = tabs,
                WeeklyTotal = tabs.Any(t => t.WeeklyCost is not null) ? tabs.Sum(t => t.WeeklyCost ?? 0) : (int?)null
            });
        }

        var daily = ProvisionerData.DailyRotationMaterials
            .Select(PriceDaily)
            .OrderBy(r => r.CostPerToken ?? int.MaxValue).ThenBy(r => r.ItemName)
            .ToList();

        return new ProvisionerView(vendors, daily);

        PricedRow PriceDaily(ProvisionerOffer offer)
        {
            byName.TryGetValue(offer.ItemName, out var m);
            var unit = m?.Sell is int s && s > 0 ? s : (int?)null;
            return new PricedRow
            {
                ItemName = offer.ItemName, ItemId = m?.Id, IconUrl = m?.IconUrl, Quantity = offer.Quantity,
                UnitPrice = unit, CostPerToken = unit is int u ? u * offer.Quantity : (int?)null
            };
        }
    }

    /// <summary>
    /// Average unit price of the <paramref name="n"/> cheapest units in the sell order book, and the
    /// total cost of buying them. Returns (null, null) for an empty book; if fewer than n are listed,
    /// averages what's there and estimates the total as average × n.
    /// </summary>
    private static (int? Average, int? Total) AverageOfCheapest(IReadOnlyList<Gw2ListingDto> sells, int n)
    {
        if (sells is null || sells.Count == 0) return (null, null);

        long total = 0;
        var taken = 0;
        foreach (var level in sells) // GW2 returns sells ascending by unit price
        {
            if (taken >= n) break;
            var take = Math.Min(level.Quantity, n - taken);
            total += (long)level.UnitPrice * take;
            taken += take;
        }
        if (taken == 0) return (null, null);

        var average = (int)Math.Round(total / (double)taken);
        var weekly = taken >= n ? (int)total : (int)Math.Round(average * (double)n);
        return (average, weekly);
    }
}
