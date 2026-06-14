using GW2Stonks.Data.Entities;

namespace GW2Stonks.Services;

/// <summary>Progress report emitted during a sync, for the UI.</summary>
public readonly record struct SyncProgress(string Phase, int Done, int Total)
{
    public double Fraction => Total <= 0 ? 0 : (double)Done / Total;
    public int Percent => (int)Math.Round(Fraction * 100);
}

/// <summary>Snapshot of stored counts and last-sync times, for the status panel.</summary>
public sealed record DashboardStatus(
    int ItemCount,
    int RecipeCount,
    int PriceCount,
    IReadOnlyDictionary<string, SyncState> States);
