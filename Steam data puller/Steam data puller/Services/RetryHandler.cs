using System.Net;

namespace SteamPuller.Services;

/// <summary>
/// Retries transient HTTP failures with exponential backoff.
///
/// Steam rate-limits <c>store.steampowered.com/api/appdetails</c> aggressively and
/// returns 429 or 5xx without warning, so a single unlucky response used to fail a
/// whole collection run. Sitting in the handler pipeline means every client
/// (Steam, SteamSpy, Supabase) gets this for free.
///
/// Timeouts are owned by this handler rather than <see cref="HttpClient.Timeout"/>,
/// because that budget covers the entire pipeline and would otherwise be consumed
/// by the backoff delays.
/// </summary>
public sealed class RetryHandler : DelegatingHandler
{
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    private readonly int      _maxAttempts;
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _attemptTimeout;

    // Injected so tests neither sleep nor depend on jitter.
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Action<string>?                         _onRetry;

    public RetryHandler(
        HttpMessageHandler inner,
        int      maxAttempts    = 4,
        TimeSpan? baseDelay     = null,
        TimeSpan? attemptTimeout = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Action<string>? onRetry = null)
        : base(inner)
    {
        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "At least one attempt is required.");

        _maxAttempts    = maxAttempts;
        _baseDelay      = baseDelay      ?? TimeSpan.FromSeconds(1);
        _attemptTimeout = attemptTimeout ?? TimeSpan.FromSeconds(30);
        _delay          = delay          ?? Task.Delay;
        _onRetry        = onRetry;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception?           failure  = null;

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attemptCts.CancelAfter(_attemptTimeout);

            try
            {
                response = await base.SendAsync(request, attemptCts.Token);
                if (!IsTransient(response.StatusCode))
                    return response;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // Caller asked us to stop; not a transient failure.
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                failure = ex; // Connection reset, DNS hiccup, or this attempt timing out.
            }

            var lastAttempt = attempt >= _maxAttempts;
            if (lastAttempt || !IsRetryable(request, response))
            {
                if (response is not null) return response;
                throw failure!;
            }

            var wait   = DelayFor(attempt, response);
            var reason = response is not null
                ? $"HTTP {(int)response.StatusCode}"
                : failure!.GetType().Name;
            response?.Dispose();

            _onRetry?.Invoke(
                $"  [RETRY] {request.Method} {request.RequestUri?.Host} → {reason}; " +
                $"attempt {attempt}/{_maxAttempts}, waiting {wait.TotalSeconds:0.#}s");

            await _delay(wait, ct);
        }
    }

    private static bool IsTransient(HttpStatusCode status) => status is
        HttpStatusCode.RequestTimeout or        // 408
        HttpStatusCode.TooManyRequests or       // 429
        HttpStatusCode.InternalServerError or   // 500
        HttpStatusCode.BadGateway or            // 502
        HttpStatusCode.ServiceUnavailable or    // 503
        HttpStatusCode.GatewayTimeout;          // 504

    /// <summary>
    /// A 429 means the server definitively rejected the request, so replaying it is
    /// always safe. Everything else is ambiguous — the request may well have been
    /// processed — so only idempotent methods are replayed. That keeps a flaky
    /// connection from inserting the same snapshot row twice.
    /// </summary>
    private static bool IsRetryable(HttpRequestMessage request, HttpResponseMessage? response)
    {
        if (response?.StatusCode == HttpStatusCode.TooManyRequests)
            return true;

        return request.Method == HttpMethod.Get
            || request.Method == HttpMethod.Head
            || request.Method == HttpMethod.Options
            || request.Method == HttpMethod.Put
            || request.Method == HttpMethod.Delete;
    }

    /// <summary>
    /// Honours <c>Retry-After</c> when the server sends it, otherwise backs off
    /// exponentially. The jitter keeps a watchlist of games from retrying in lockstep.
    /// </summary>
    private TimeSpan DelayFor(int attempt, HttpResponseMessage? response)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
            return Min(delta, MaxDelay);

        if (retryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero) return Min(until, MaxDelay);
        }

        var backoff = _baseDelay * Math.Pow(2, attempt - 1);
        var jitter  = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
        return Min(backoff + jitter, MaxDelay);
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
