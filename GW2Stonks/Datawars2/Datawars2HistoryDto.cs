using System.Text.Json.Serialization;

namespace GW2Stonks.Datawars2;

/// <summary>One day's aggregated trading-post stats for an item from datawars2.ie's history API.</summary>
public sealed class Datawars2HistoryDto
{
    [JsonPropertyName("itemID")] public int ItemId { get; set; }

    [JsonPropertyName("date")] public DateTime Date { get; set; }

    /// <summary>Units bought off sellers that day (a listing's "sells").</summary>
    [JsonPropertyName("sell_sold")] public int SellSold { get; set; }

    /// <summary>Units sold into buy orders that day.</summary>
    [JsonPropertyName("buy_sold")] public int BuySold { get; set; }

    /// <summary>Average total quantity listed for sale that day (supply).</summary>
    [JsonPropertyName("sell_quantity_avg")] public double SellQuantityAvg { get; set; }

    /// <summary>Average total quantity in buy orders that day (demand).</summary>
    [JsonPropertyName("buy_quantity_avg")] public double BuyQuantityAvg { get; set; }

    /// <summary>Number of samples that contributed to this day (lower = a still-forming current day).</summary>
    [JsonPropertyName("count")] public int Count { get; set; }
}
