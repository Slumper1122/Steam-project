using System.Net;
using System.Net.Http.Headers;
using SteamPuller.Services;

namespace Steam.Tests;

public class RetryHandlerTests
{
    // ── Transient statuses ────────────────────────────────────────────────────

    [Fact]
    public async Task TransientStatuses_AreRetriedUntilSuccess()
    {
        var inner = new ScriptedHandler(
            Status(HttpStatusCode.TooManyRequests),
            Status(HttpStatusCode.ServiceUnavailable),
            Status(HttpStatusCode.OK));

        var (client, _) = ClientFor(inner);
        var response = await client.GetAsync("https://store.steampowered.com/api/appdetails");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task EachTransientStatus_TriggersARetry(HttpStatusCode transient)
    {
        var inner = new ScriptedHandler(Status(transient), Status(HttpStatusCode.OK));

        var (client, _) = ClientFor(inner);
        var response = await client.GetAsync("https://example.test/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task NonTransientStatuses_AreReturnedImmediately(HttpStatusCode status)
    {
        var inner = new ScriptedHandler(Status(status));

        var (client, delays) = ClientFor(inner);
        var response = await client.GetAsync("https://example.test/");

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(1, inner.Calls);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task GivingUp_ReturnsTheLastResponseRatherThanThrowing()
    {
        var inner = new ScriptedHandler(
            Status(HttpStatusCode.ServiceUnavailable),
            Status(HttpStatusCode.ServiceUnavailable),
            Status(HttpStatusCode.ServiceUnavailable));

        var (client, _) = ClientFor(inner, maxAttempts: 3);
        var response = await client.GetAsync("https://example.test/");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task SingleAttemptConfiguration_NeverRetries()
    {
        var inner = new ScriptedHandler(
            Status(HttpStatusCode.TooManyRequests),
            Status(HttpStatusCode.OK));

        var (client, _) = ClientFor(inner, maxAttempts: 1);
        var response = await client.GetAsync("https://example.test/");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    // ── Network-level failures ────────────────────────────────────────────────

    [Fact]
    public async Task ConnectionFailure_IsRetried()
    {
        var inner = new ScriptedHandler(
            Throws(new HttpRequestException("Connection reset by peer")),
            Status(HttpStatusCode.OK));

        var (client, _) = ClientFor(inner);
        var response = await client.GetAsync("https://example.test/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task ConnectionFailure_OnLastAttempt_Rethrows()
    {
        var inner = new ScriptedHandler(
            Throws(new HttpRequestException("boom")),
            Throws(new HttpRequestException("boom")));

        var (client, _) = ClientFor(inner, maxAttempts: 2);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("https://example.test/"));

        Assert.Equal("boom", ex.Message);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task AttemptTimeout_IsTreatedAsTransient()
    {
        var inner = new ScriptedHandler(
            Hangs(),
            Status(HttpStatusCode.OK));

        var (client, _) = ClientFor(inner, attemptTimeout: TimeSpan.FromMilliseconds(50));
        var response = await client.GetAsync("https://example.test/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ServerError_OnPost_IsNotRetried()
    {
        // The insert may already have landed, so replaying it could duplicate a row.
        var inner = new ScriptedHandler(
            Status(HttpStatusCode.InternalServerError),
            Status(HttpStatusCode.Created));

        var (client, _) = ClientFor(inner);
        var response = await client.PostAsync("https://x.supabase.co/rest/v1/snapshots",
                                              new StringContent("{}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task ConnectionFailure_OnPost_IsNotRetried()
    {
        var inner = new ScriptedHandler(
            Throws(new HttpRequestException("reset")),
            Status(HttpStatusCode.Created));

        var (client, _) = ClientFor(inner);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.PostAsync("https://x.supabase.co/rest/v1/snapshots",
                                   new StringContent("{}")));

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task RateLimit_OnPost_IsRetried()
    {
        // 429 means the server rejected it outright, so replaying is safe.
        var inner = new ScriptedHandler(
            Status(HttpStatusCode.TooManyRequests),
            Status(HttpStatusCode.Created));

        var (client, _) = ClientFor(inner);
        var response = await client.PostAsync("https://x.supabase.co/rest/v1/snapshots",
                                              new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    // ── Backoff ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Backoff_DoublesEachAttempt()
    {
        var inner = new ScriptedHandler(
            Status(HttpStatusCode.BadGateway),
            Status(HttpStatusCode.BadGateway),
            Status(HttpStatusCode.BadGateway),
            Status(HttpStatusCode.OK));

        var (client, delays) = ClientFor(inner, baseDelay: TimeSpan.FromSeconds(1));
        await client.GetAsync("https://example.test/");

        Assert.Equal(3, delays.Count);
        AssertJittered(TimeSpan.FromSeconds(1), delays[0]);
        AssertJittered(TimeSpan.FromSeconds(2), delays[1]);
        AssertJittered(TimeSpan.FromSeconds(4), delays[2]);
    }

    [Fact]
    public async Task RetryAfterSeconds_OverridesBackoff()
    {
        var rateLimited = Status(HttpStatusCode.TooManyRequests);
        rateLimited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
        var inner = new ScriptedHandler(rateLimited, Status(HttpStatusCode.OK));

        var (client, delays) = ClientFor(inner, baseDelay: TimeSpan.FromSeconds(1));
        await client.GetAsync("https://example.test/");

        Assert.Equal(TimeSpan.FromSeconds(7), Assert.Single(delays));
    }

    [Fact]
    public async Task RetryAfterDate_OverridesBackoff()
    {
        var rateLimited = Status(HttpStatusCode.ServiceUnavailable);
        rateLimited.Headers.RetryAfter =
            new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(10));
        var inner = new ScriptedHandler(rateLimited, Status(HttpStatusCode.OK));

        var (client, delays) = ClientFor(inner, baseDelay: TimeSpan.FromSeconds(1));
        await client.GetAsync("https://example.test/");

        // Second-resolution header, so allow for the time spent in the test itself.
        Assert.InRange(Assert.Single(delays).TotalSeconds, 8, 10);
    }

    [Fact]
    public async Task ElapsedRetryAfterDate_FallsBackToBackoff()
    {
        var rateLimited = Status(HttpStatusCode.ServiceUnavailable);
        rateLimited.Headers.RetryAfter =
            new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(-30));
        var inner = new ScriptedHandler(rateLimited, Status(HttpStatusCode.OK));

        var (client, delays) = ClientFor(inner, baseDelay: TimeSpan.FromSeconds(1));
        await client.GetAsync("https://example.test/");

        AssertJittered(TimeSpan.FromSeconds(1), Assert.Single(delays));
    }

    [Fact]
    public async Task Backoff_IsCappedAtThirtySeconds()
    {
        var inner = new ScriptedHandler(
            Status(HttpStatusCode.BadGateway),
            Status(HttpStatusCode.OK));

        var (client, delays) = ClientFor(inner, baseDelay: TimeSpan.FromMinutes(5));
        await client.GetAsync("https://example.test/");

        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(delays));
    }

    [Fact]
    public async Task HugeRetryAfter_IsAlsoCapped()
    {
        var rateLimited = Status(HttpStatusCode.TooManyRequests);
        rateLimited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
        var inner = new ScriptedHandler(rateLimited, Status(HttpStatusCode.OK));

        var (client, delays) = ClientFor(inner);
        await client.GetAsync("https://example.test/");

        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(delays));
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancellation_IsNotRetried()
    {
        var inner = new ScriptedHandler(Hangs(), Status(HttpStatusCode.OK));
        using var cts = new CancellationTokenSource();

        var (client, delays) = ClientFor(inner);
        var pending = client.GetAsync("https://example.test/", cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, inner.Calls);
        Assert.Empty(delays);
    }

    // ── Construction ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveAttemptCount_IsRejected(int maxAttempts)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new RetryHandler(new ScriptedHandler(), maxAttempts: maxAttempts));

    [Fact]
    public async Task FactoryClient_RetriesTransientFailures()
    {
        // Guards the wiring: a client built by the factory must inherit the retries.
        var inner = new ScriptedHandler(
            Status(HttpStatusCode.TooManyRequests),
            Status(HttpStatusCode.OK));

        using var client = HttpClientFactory.Create(inner);
        var response = await client.GetAsync("https://store.steampowered.com/api/appdetails");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Builds a client whose retry delays are recorded instead of awaited.</summary>
    private static (HttpClient Client, List<TimeSpan> Delays) ClientFor(
        ScriptedHandler inner,
        int       maxAttempts    = 4,
        TimeSpan? baseDelay      = null,
        TimeSpan? attemptTimeout = null)
    {
        var delays = new List<TimeSpan>();
        var handler = new RetryHandler(
            inner,
            maxAttempts:    maxAttempts,
            baseDelay:      baseDelay,
            attemptTimeout: attemptTimeout,
            delay:          (wait, _) => { delays.Add(wait); return Task.CompletedTask; });

        return (new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, delays);
    }

    private static void AssertJittered(TimeSpan expected, TimeSpan actual)
        => Assert.InRange(actual, expected, expected + TimeSpan.FromMilliseconds(250));

    private static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    private static Func<CancellationToken, Task<HttpResponseMessage>> Throws(Exception ex)
        => _ => throw ex;

    /// <summary>Never completes on its own, so the per-attempt timeout has to fire.</summary>
    private static Func<CancellationToken, Task<HttpResponseMessage>> Hangs()
        => async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

    /// <summary>Replays a fixed script of responses, one per request, and counts calls.</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _script;

        public ScriptedHandler(params object[] steps)
        {
            _script = new Queue<Func<CancellationToken, Task<HttpResponseMessage>>>(
                steps.Select<object, Func<CancellationToken, Task<HttpResponseMessage>>>(step => step switch
                {
                    HttpResponseMessage response => _ => Task.FromResult(response),
                    Func<CancellationToken, Task<HttpResponseMessage>> f => f,
                    _ => throw new ArgumentException($"Unsupported step: {step.GetType()}", nameof(steps)),
                }));
        }

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (_script.Count == 0)
                throw new InvalidOperationException($"Unexpected request #{Calls} to {request.RequestUri}.");

            return _script.Dequeue()(ct);
        }
    }
}
