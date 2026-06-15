using System.Net.Http.Json;
using System.Text.Json;

namespace GW2Stonks.Datawars2;

/// <summary>Typed client for the datawars2.ie GW2 trading-post data API.</summary>
public sealed class Datawars2Client
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public Datawars2Client(HttpClient http) => _http = http;

    /// <summary>Daily history rows for the given items, from <paramref name="start"/> (inclusive) onward.</summary>
    public async Task<List<Datawars2HistoryDto>> GetHistoryAsync(
        IEnumerable<int> ids, DateOnly start, CancellationToken ct = default)
    {
        var idList = string.Join(',', ids);
        if (idList.Length == 0) return new List<Datawars2HistoryDto>();

        var url = $"gw2/v2/history/json?itemID={idList}&start={start:yyyy-MM-dd}";
        var result = await _http.GetFromJsonAsync<List<Datawars2HistoryDto>>(url, Json, ct);
        return result ?? new List<Datawars2HistoryDto>();
    }
}
