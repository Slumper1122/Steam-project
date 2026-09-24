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
    /// </summary>
    public static HttpClient Create(HttpMessageHandler? handler = null)
    {
        var client = handler is null
            ? new HttpClient()
            : new HttpClient(handler, disposeHandler: false);

        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SteamDataPuller/1.0");
        return client;
    }
}
