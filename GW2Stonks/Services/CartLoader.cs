namespace GW2Stonks.Services;

/// <summary>Loads the persisted cart into <see cref="CartService"/> once when the web host starts.</summary>
public sealed class CartLoader : IHostedService
{
    private readonly CartService _cart;
    private readonly ILogger<CartLoader> _log;

    public CartLoader(CartService cart, ILogger<CartLoader> log)
    {
        _cart = cart;
        _log = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try { await _cart.LoadAsync(cancellationToken); }
        catch (Exception ex) { _log.LogWarning(ex, "Could not load persisted cart at startup"); }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
