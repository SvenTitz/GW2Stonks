namespace GW2Stonks.Data.Entities;

/// <summary>Tracks when each data set was last synced from the GW2 API.</summary>
public class SyncState
{
    /// <summary>Logical data set name and primary key, e.g. "items", "recipes", "prices".</summary>
    public string Key { get; set; } = "";

    public DateTime LastSyncedUtc { get; set; }

    /// <summary>Number of records stored at the last sync.</summary>
    public int RecordCount { get; set; }
}
