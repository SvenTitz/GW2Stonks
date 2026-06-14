namespace GW2Stonks.Gw2Api;

/// <summary>Configuration for the GW2 API client and sync, bound from the "Gw2" section.</summary>
public sealed class Gw2Options
{
    public const string SectionName = "Gw2";

    public string ApiBaseUrl { get; set; } = "https://api.guildwars2.com/";

    /// <summary>How often the background service refreshes all trading-post prices.</summary>
    public int PriceRefreshMinutes { get; set; } = 5;

    /// <summary>Max concurrent API requests during a sync.</summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>Client-side rate limit cap (GW2 allows ~600/min; we stay well under).</summary>
    public int RequestsPerSecond { get; set; } = 8;

    /// <summary>Ids per batched request (API maximum is 200).</summary>
    public int BatchSize { get; set; } = 200;
}
