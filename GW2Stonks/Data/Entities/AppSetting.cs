namespace GW2Stonks.Data.Entities;

/// <summary>
/// Generic persisted key/value setting. Currently holds the GW2 account API key so it
/// survives restarts (this is a local-only tool; the value is stored as-is in the local DB).
/// </summary>
public class AppSetting
{
    /// <summary>Logical setting name and primary key, e.g. "gw2.apikey".</summary>
    public string Key { get; set; } = "";

    public string Value { get; set; } = "";
}
