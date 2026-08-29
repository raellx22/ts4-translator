using System.Net;

namespace TS4Translator.Providers.Tests;

/// <summary>Test double standing in for a real HTTP server, so provider tests never touch the network (brief section 44).</summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly IReadOnlyList<(HttpStatusCode StatusCode, string Body)> _responses;

    public FakeHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
        : this([(statusCode, responseBody)])
    {
    }

    /// <summary>
    /// Returns one response per call, in order - useful for simulating a transient failure that
    /// recovers on retry (e.g. 503 then 200). The last entry repeats for any call beyond the list's
    /// length, so a test doesn't need to guess exactly how many retry attempts will happen.
    /// </summary>
    public FakeHttpMessageHandler(IReadOnlyList<(HttpStatusCode StatusCode, string Body)> responses)
    {
        ArgumentNullException.ThrowIfNull(responses);
        if (responses.Count == 0)
        {
            throw new ArgumentException("At least one response is required.", nameof(responses));
        }

        _responses = responses;
    }

    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }
    public int CallCount { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var (statusCode, responseBody) = _responses[Math.Min(CallCount, _responses.Count - 1)];
        CallCount++;
        LastRequest = request;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody),
        };
    }
}
