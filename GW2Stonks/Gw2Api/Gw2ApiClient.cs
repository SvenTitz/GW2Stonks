using System.Net.Http.Json;
using System.Text.Json;

namespace GW2Stonks.Gw2Api;

/// <summary>Thin typed client over the public GW2 API v2 endpoints used by the app.</summary>
public sealed class Gw2ApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _http;

    public Gw2ApiClient(HttpClient http) => _http = http;

    public Task<List<int>> GetItemIdsAsync(CancellationToken ct = default) =>
        GetListAsync<int>("v2/items", ct);

    public Task<List<int>> GetRecipeIdsAsync(CancellationToken ct = default) =>
        GetListAsync<int>("v2/recipes", ct);

    public Task<List<int>> GetPriceIdsAsync(CancellationToken ct = default) =>
        GetListAsync<int>("v2/commerce/prices", ct);

    public Task<List<Gw2ItemDto>> GetItemsAsync(IEnumerable<int> ids, CancellationToken ct = default) =>
        GetByIdsAsync<Gw2ItemDto>("v2/items", ids, ct);

    public Task<List<Gw2RecipeDto>> GetRecipesAsync(IEnumerable<int> ids, CancellationToken ct = default) =>
        GetByIdsAsync<Gw2RecipeDto>("v2/recipes", ids, ct);

    public Task<List<Gw2PriceDto>> GetPricesAsync(IEnumerable<int> ids, CancellationToken ct = default) =>
        GetByIdsAsync<Gw2PriceDto>("v2/commerce/prices", ids, ct);

    public Task<List<Gw2ListingsDto>> GetListingsAsync(IEnumerable<int> ids, CancellationToken ct = default) =>
        GetByIdsAsync<Gw2ListingsDto>("v2/commerce/listings", ids, ct);

    private async Task<List<T>> GetListAsync<T>(string path, CancellationToken ct)
    {
        var result = await _http.GetFromJsonAsync<List<T>>(path, Json, ct);
        return result ?? new List<T>();
    }

    private async Task<List<T>> GetByIdsAsync<T>(string path, IEnumerable<int> ids, CancellationToken ct)
    {
        var idList = string.Join(',', ids);
        if (idList.Length == 0) return new List<T>();
        var result = await _http.GetFromJsonAsync<List<T>>($"{path}?ids={idList}", Json, ct);
        return result ?? new List<T>();
    }
}
