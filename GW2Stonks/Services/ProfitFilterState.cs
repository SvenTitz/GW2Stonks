namespace GW2Stonks.Services;

/// <summary>
/// Holds the Craft profit page's filter selections so they survive navigating away and back.
/// Registered as a singleton (single-user local tool).
/// </summary>
public sealed class ProfitFilterState
{
    public PricingMode Mode { get; set; } = PricingMode.InstantBuy;
    public string? NameFilter { get; set; }
    public IEnumerable<string> SelectedDisciplines { get; set; } = new List<string>();
    public IEnumerable<string> SelectedTypes { get; set; } = new List<string>();
    public int MinProfitCopper { get; set; }
    public double MinMargin { get; set; }
    public int MinSoldPerDay { get; set; }
    public double MaxSellThrough { get; set; }
    public int MinRelists { get; set; }
    public bool ProfitableOnly { get; set; } = true;
    public int FillPercent { get; set; } = 20;
}
