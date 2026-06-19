using System.Net.Http.Headers;
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

    // ── Authenticated account endpoints (require an API key passed as a bearer token) ──────────

    /// <summary>Validate a key and read its scopes; null on an invalid/unauthorized key.</summary>
    public Task<Gw2TokenInfoDto?> GetTokenInfoAsync(string apiKey, CancellationToken ct = default) =>
        GetAuthedAsync<Gw2TokenInfoDto>("v2/tokeninfo", apiKey, ct);

    /// <summary>Material storage stacks (/v2/account/materials).</summary>
    public async Task<List<Gw2ItemSlotDto>> GetMaterialsAsync(string apiKey, CancellationToken ct = default) =>
        await GetAuthedAsync<List<Gw2ItemSlotDto>>("v2/account/materials", apiKey, ct) ?? new();

    /// <summary>Bank slots (/v2/account/bank); empty slots come back as null.</summary>
    public async Task<List<Gw2ItemSlotDto?>> GetBankAsync(string apiKey, CancellationToken ct = default) =>
        await GetAuthedAsync<List<Gw2ItemSlotDto?>>("v2/account/bank", apiKey, ct) ?? new();

    /// <summary>Shared inventory slots (/v2/account/inventory); empty slots come back as null.</summary>
    public async Task<List<Gw2ItemSlotDto?>> GetSharedInventoryAsync(string apiKey, CancellationToken ct = default) =>
        await GetAuthedAsync<List<Gw2ItemSlotDto?>>("v2/account/inventory", apiKey, ct) ?? new();

    /// <summary>Character names on the account (/v2/characters).</summary>
    public async Task<List<string>> GetCharacterNamesAsync(string apiKey, CancellationToken ct = default) =>
        await GetAuthedAsync<List<string>>("v2/characters", apiKey, ct) ?? new();

    /// <summary>One character's bags + their slots (/v2/characters/{name}/inventory).</summary>
    public Task<Gw2CharacterInventoryDto?> GetCharacterInventoryAsync(string apiKey, string name, CancellationToken ct = default) =>
        GetAuthedAsync<Gw2CharacterInventoryDto>($"v2/characters/{Uri.EscapeDataString(name)}/inventory", apiKey, ct);

    private async Task<T?> GetAuthedAsync<T>(string path, string apiKey, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<T>(Json, ct);
    }

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
