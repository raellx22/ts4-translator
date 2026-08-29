using System.Net;

namespace TS4Translator.Providers.Resilience;

/// <summary>
/// Retries an HTTP call with exponential backoff + jitter on a transient failure (5xx, 408, 429, or a
/// network-level/timeout exception) - translation provider APIs, especially free-tier LLM endpoints
/// like Gemini, routinely return 503 under load even with quota to spare (brief section 11's
/// "retry/backoff" requirement; confirmed necessary by a real failure a user hit mid-translation, not
/// a hypothetical). Never retries a 4xx that isn't 408/429 (a bad request or bad auth won't succeed on
/// retry) and never retries once the caller's own <see cref="CancellationToken"/> is cancelled (the
/// user's "Cancelar tradução" button must not be silently swallowed as a retryable failure).
/// </summary>
public static class HttpRetry
{
    /// <param name="sendRequest">
    /// Builds and sends one attempt's <see cref="HttpRequestMessage"/> from scratch. Must not close
    /// over an <see cref="HttpRequestMessage"/> built outside this delegate - <see cref="HttpClient"/>
    /// refuses to send the same request message instance twice, so every retry needs a fresh one.
    /// </param>
    /// <param name="delayAsync">Exposed for tests; production callers should omit it (real delay).</param>
    public static async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> sendRequest,
        CancellationToken cancellationToken,
        int maxRetries = 3,
        Func<int, CancellationToken, Task>? delayAsync = null)
    {
        delayAsync ??= DefaultDelayAsync;

        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            try
            {
                response = await sendRequest(cancellationToken).ConfigureAwait(false);
                if (attempt >= maxRetries || !IsTransientFailure(response.StatusCode))
                {
                    return response;
                }
            }
            catch (Exception exception) when (attempt < maxRetries && IsRetryableException(exception, cancellationToken))
            {
            }

            response?.Dispose();
            await delayAsync(attempt, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task DefaultDelayAsync(int attempt, CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromMilliseconds(300 * Math.Pow(2, attempt));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
        return Task.Delay(backoff + jitter, cancellationToken);
    }

    private static bool IsTransientFailure(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.RequestTimeout or
        HttpStatusCode.TooManyRequests or
        HttpStatusCode.InternalServerError or
        HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.GatewayTimeout;

    private static bool IsRetryableException(Exception exception, CancellationToken cancellationToken)
    {
        // A TaskCanceledException here is either the caller's own cancellation (must never be
        // retried/swallowed) or an HttpClient request timeout wrapping one (retryable, same as any
        // other transient network failure) - the only way to tell them apart is checking whose token
        // actually fired.
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return exception is HttpRequestException or TaskCanceledException or TimeoutException;
    }
}
