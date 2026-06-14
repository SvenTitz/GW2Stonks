using System.Threading.RateLimiting;

namespace GW2Stonks.Gw2Api;

/// <summary>
/// Throttles outgoing requests through a shared token-bucket limiter so we stay
/// comfortably under the GW2 API rate limit. Sits inside the resilience handler so
/// each retry attempt also acquires a token.
/// </summary>
public sealed class RateLimitingHandler : DelegatingHandler
{
    private readonly RateLimiter _limiter;

    public RateLimitingHandler(RateLimiter limiter) => _limiter = limiter;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using RateLimitLease lease = await _limiter.AcquireAsync(1, cancellationToken);
        return await base.SendAsync(request, cancellationToken);
    }
}
