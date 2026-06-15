namespace GW2Stonks.Datawars2;

/// <summary>Configuration for the datawars2.ie volume source, bound from the "Datawars2" section.</summary>
public sealed class Datawars2Options
{
    public const string SectionName = "Datawars2";

    public string ApiBaseUrl { get; set; } = "https://api.datawars2.ie/";

    /// <summary>How often the background service refreshes cached volume data.</summary>
    public int RefreshHours { get; set; } = 12;

    /// <summary>Item ids per history request (the endpoint accepts ~250).</summary>
    public int BatchSize { get; set; } = 250;

    /// <summary>Days of daily history to average into a sold/day figure.</summary>
    public int HistoryDays { get; set; } = 3;

    public int MaxConcurrency { get; set; } = 4;
}
