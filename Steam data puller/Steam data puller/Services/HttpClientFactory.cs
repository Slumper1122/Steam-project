using System.Net.Http.Headers;

namespace SteamPuller.Services;

/// <summary>
/// Single place where the outbound HTTP client is configured, so every command
/// uses the same timeout and User-Agent.
/// </summary>
public static class HttpClientFactory
{
    /// <summary>
    /// Creates the client used for all Steam, SteamSpy and Supabase calls.
    /// Pass a handler to redirect traffic in tests; the caller keeps ownership
    /// of it, so disposing the returned client leaves the handler intact.
    /// Passing a <see cref="RetryHandler"/> supplies the whole pipeline, which is
    /// how tests shorten the backoff instead of sleeping through it.
    /// </summary>
    public static HttpClient Create(HttpMessageHandler? handler = null)
    {
        var pipeline = handler as RetryHandler ?? new RetryHandler(
            handler ?? new HttpClientHandler(),
            onRetry: msg =>
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(msg);
                Console.ResetColor();
            });

        // RetryHandler enforces a per-attempt timeout. A client-level timeout would
        // cover the retries and their backoff too, and cut them short.
        return new HttpClient(pipeline, disposeHandler: handler is null)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestHeaders = { UserAgent = { ProductInfoHeaderValue.Parse("SteamDataPuller/1.0") } },
        };
    }
}
