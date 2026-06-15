namespace GW2Stonks.Util;

/// <summary>Formats copper amounts into GW2's gold / silver / copper notation.</summary>
public static class Coin
{
    public static string Format(int? copper)
    {
        if (copper is null) return "—";

        int c = copper.Value;
        if (c == 0) return "0c";

        string sign = c < 0 ? "-" : "";
        c = Math.Abs(c);
        int gold = c / 10000;
        int silver = c % 10000 / 100;
        int cop = c % 100;

        var parts = new List<string>(3);
        if (gold > 0) parts.Add($"{gold}g");
        if (silver > 0) parts.Add($"{silver}s");
        if (cop > 0) parts.Add($"{cop}c");
        return sign + string.Join(' ', parts);
    }
}
