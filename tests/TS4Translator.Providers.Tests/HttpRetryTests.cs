using System.Net;
using TS4Translator.Providers.Resilience;

namespace TS4Translator.Providers.Tests;

/// <summary>
/// Exercises <see cref="HttpRetry"/> directly with an instant injected delay, so retry/backoff logic
/// is fully covered without any test taking real wall-clock time for backoff - provider-level tests
/// (OpenAICompatibleProviderTests etc.) only need to prove they wire this helper up correctly, not
/// re-verify every edge case of the retry policy itself.
/// </summary>
public class HttpRetryTests
{
    private static Task NoDelay(int attempt, CancellationToken cancellationToken) => Task.CompletedTask;

    private static HttpResponseMessage Response(HttpStatusCode statusCode) => new(statusCode);

    [Fact]
    public async Task SendWithRetryAsync_FirstAttemptSucceeds_ReturnsItWithoutRetrying()
    {
        var callCount = 0;

        var response = await HttpRetry.SendWithRetryAsync(
            ct => { callCount++; return Task.FromResult(Response(HttpStatusCode.OK)); },
            CancellationToken.None,
            maxRetries: 3,
            delayAsync: NoDelay);

        Assert.Equal(1, callCount);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task SendWithRetryAsync_TransientStatusCode_RetriesUntilSuccess(HttpStatusCode transientStatus)
    {
        var callCount = 0;

        var response = await HttpRetry.SendWithRetryAsync(
            ct =>
            {
                callCount++;
                return Task.FromResult(callCount < 3 ? Response(transientStatus) : Response(HttpStatusCode.OK));
            },
            CancellationToken.None,
            maxRetries: 5,
            delayAsync: NoDelay);

        Assert.Equal(3, callCount);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task SendWithRetryAsync_NonTransientStatusCode_ReturnsImmediatelyWithoutRetrying(HttpStatusCode permanentStatus)
    {
        var callCount = 0;

        var response = await HttpRetry.SendWithRetryAsync(
            ct => { callCount++; return Task.FromResult(Response(permanentStatus)); },
            CancellationToken.None,
            maxRetries: 5,
            delayAsync: NoDelay);

        Assert.Equal(1, callCount);
        Assert.Equal(permanentStatus, response.StatusCode);
    }

    [Fact]
    public async Task SendWithRetryAsync_TransientStatusCodePersistsPastMaxRetries_ReturnsLastFailedResponse()
    {
        var callCount = 0;

        var response = await HttpRetry.SendWithRetryAsync(
            ct => { callCount++; return Task.FromResult(Response(HttpStatusCode.ServiceUnavailable)); },
            CancellationToken.None,
            maxRetries: 2,
            delayAsync: NoDelay);

        Assert.Equal(3, callCount); // initial attempt + 2 retries
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task SendWithRetryAsync_MaxRetriesZero_NeverRetries()
    {
        var callCount = 0;

        var response = await HttpRetry.SendWithRetryAsync(
            ct => { callCount++; return Task.FromResult(Response(HttpStatusCode.ServiceUnavailable)); },
            CancellationToken.None,
            maxRetries: 0,
            delayAsync: NoDelay);

        Assert.Equal(1, callCount);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task SendWithRetryAsync_HttpRequestExceptionThenSuccess_RetriesAndSucceeds()
    {
        var callCount = 0;

        var response = await HttpRetry.SendWithRetryAsync(
            ct =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new HttpRequestException("connection reset");
                }

                return Task.FromResult(Response(HttpStatusCode.OK));
            },
            CancellationToken.None,
            maxRetries: 3,
            delayAsync: NoDelay);

        Assert.Equal(2, callCount);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SendWithRetryAsync_HttpRequestExceptionPersistsPastMaxRetries_PropagatesTheException()
    {
        var callCount = 0;

        await Assert.ThrowsAsync<HttpRequestException>(() => HttpRetry.SendWithRetryAsync(
            ct => { callCount++; throw new HttpRequestException("still down"); },
            CancellationToken.None,
            maxRetries: 2,
            delayAsync: NoDelay));

        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task SendWithRetryAsync_CallerCancellationRequested_DoesNotRetryAndPropagates()
    {
        using var cts = new CancellationTokenSource();
        var callCount = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HttpRetry.SendWithRetryAsync(
            ct =>
            {
                callCount++;
                cts.Cancel();
                throw new TaskCanceledException("request timed out");
            },
            cts.Token,
            maxRetries: 3,
            delayAsync: NoDelay));

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task SendWithRetryAsync_RequestTimeoutWithoutCallerCancellation_IsRetried()
    {
        // A TaskCanceledException that is NOT the caller's own token firing (an HttpClient.Timeout,
        // for example) must be treated as a transient failure and retried, not mistaken for a
        // deliberate cancellation.
        var callCount = 0;

        var response = await HttpRetry.SendWithRetryAsync(
            ct =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new TaskCanceledException("request timed out");
                }

                return Task.FromResult(Response(HttpStatusCode.OK));
            },
            CancellationToken.None,
            maxRetries: 3,
            delayAsync: NoDelay);

        Assert.Equal(2, callCount);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
