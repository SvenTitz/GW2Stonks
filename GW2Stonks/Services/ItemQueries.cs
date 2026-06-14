using GW2Stonks.Data.Entities;
using GW2Stonks.Models;

namespace GW2Stonks.Services;

/// <summary>Shared, EF-translatable projections over items so the grid and tooling agree.</summary>
public static class ItemQueries
{
    /// <summary>Project items (left-joined to their prices) into the flat grid row shape.</summary>
    public static IQueryable<ItemListRow> ToItemRows(this IQueryable<Item> items) =>
        items.Select(i => new ItemListRow
        {
            Id = i.Id,
            Name = i.Name,
            Type = i.Type,
            Rarity = i.Rarity,
            Level = i.Level,
            IconUrl = i.IconUrl,
            Buy = i.Price != null ? i.Price.BuyUnitPrice : (int?)null,
            Sell = i.Price != null ? i.Price.SellUnitPrice : (int?)null,
            PriceUpdatedUtc = i.Price != null ? i.Price.UpdatedUtc : (DateTime?)null
        });
}
