using System.Globalization;
using System.Net;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.GoogleTranslate;

namespace TS4Translator.Providers.Tests;

public sealed class GoogleTranslateProviderTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>The real endpoint's shape: an array of sentence chunks, each an array whose first element is the translated text.</summary>
    private const string SingleSentenceResponse = """[[["Raso","Shallow",null,null,10]],null,"en"]""";

    [Fact]
    public void ExtractTranslation_SingleSentence_ReturnsTheTranslatedText()
        => Assert.Equal("Raso", GoogleTranslateProvider.ExtractTranslation(SingleSentenceResponse));

    [Fact]
    public void ExtractTranslation_SeveralChunks_ConcatenatesThemInOrder()
    {
        const string payload = """[[["Primeira frase. ","First sentence. ",null,null,10],["Segunda frase.","Second sentence.",null,null,10]],null,"en"]""";

        Assert.Equal("Primeira frase. Segunda frase.", GoogleTranslateProvider.ExtractTranslation(payload));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""[null,null,"en"]""")]
    public void ExtractTranslation_UnusablePayload_ReturnsNullRatherThanGuessing(string payload)
        => Assert.Null(GoogleTranslateProvider.ExtractTranslation(payload));

    [Fact]
    public async Task TranslateBatchAsync_ReturnsOneResultPerItem()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SingleSentenceResponse);
        var provider = new GoogleTranslateProvider(new HttpClient(handler));

        var results = await provider.TranslateBatchAsync(
            [new TranslationRequestItem("1", "Shallow"), new TranslationRequestItem("2", "Average Hips")],
            English,
            PortugueseBrazil);

        Assert.Equal(2, results.Count);
        Assert.Equal(["1", "2"], results.Select(r => r.Id).Order());
    }

    /// <summary>No API key, no endpoint, no model — the whole point of offering it next to Argos.</summary>
    [Fact]
    public void Capabilities_RequireNoCredential()
    {
        var provider = new GoogleTranslateProvider(new HttpClient());

        Assert.False(provider.Capabilities.RequiresApiKey);
    }

    [Fact]
    public async Task TranslateBatchAsync_TargetsBrazilianPortuguese_SendsTheRegionalCode()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SingleSentenceResponse);
        var provider = new GoogleTranslateProvider(new HttpClient(handler));

        await provider.TranslateBatchAsync([new TranslationRequestItem("1", "Shallow")], English, PortugueseBrazil);

        var query = handler.LastRequest!.RequestUri!.Query;
        Assert.Contains("sl=en", query, StringComparison.Ordinal);
        Assert.Contains("tl=pt-BR", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslateBatchAsync_ResponseWithoutAUsableTranslation_DropsThatItem()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "[]");
        var provider = new GoogleTranslateProvider(new HttpClient(handler));

        var results = await provider.TranslateBatchAsync(
            [new TranslationRequestItem("1", "Shallow")], English, PortugueseBrazil);

        // Dropped, not invented: TranslationOrchestrator then reports it as a per-item failure.
        Assert.Empty(results);
    }

    [Fact]
    public async Task TranslateBatchAsync_ServerError_Throws()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Forbidden, string.Empty);
        var provider = new GoogleTranslateProvider(new HttpClient(handler), maxRetries: 0);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.TranslateBatchAsync(
            [new TranslationRequestItem("1", "Shallow")], English, PortugueseBrazil));
    }

    /// <summary>This endpoint throttles by IP, so a 429 has to be retried rather than fail the run.</summary>
    [Fact]
    public async Task TranslateBatchAsync_ThrottledThenRecovers_RetriesAndSucceeds()
    {
        var handler = new FakeHttpMessageHandler(
        [
            (HttpStatusCode.TooManyRequests, string.Empty),
            (HttpStatusCode.OK, SingleSentenceResponse),
        ]);

        var provider = new GoogleTranslateProvider(new HttpClient(handler));
        var results = await provider.TranslateBatchAsync(
            [new TranslationRequestItem("1", "Shallow")], English, PortugueseBrazil);

        Assert.Equal("Raso", Assert.Single(results).TranslatedProtectedText);
        Assert.True(handler.CallCount > 1);
    }

    [Fact]
    public async Task TranslateBatchAsync_NoItems_MakesNoRequest()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SingleSentenceResponse);
        var provider = new GoogleTranslateProvider(new HttpClient(handler));

        var results = await provider.TranslateBatchAsync([], English, PortugueseBrazil);

        Assert.Empty(results);
        Assert.Equal(0, handler.CallCount);
    }
    /// <summary>
    /// Verified against the live endpoint: with no User-Agent it answers 429 with an HTML error page
    /// on the first request. Losing this header would break the provider completely.
    /// </summary>
    [Fact]
    public async Task TranslateBatchAsync_SendsAUserAgent()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SingleSentenceResponse);
        var provider = new GoogleTranslateProvider(new HttpClient(handler));

        await provider.TranslateBatchAsync([new TranslationRequestItem("1", "Shallow")], English, PortugueseBrazil);

        Assert.True(handler.LastRequest!.Headers.Contains("User-Agent"));
    }
    /// <summary>
    /// Measured against the live service: sustained use gets throttled. One refused string must not
    /// throw away the rest of a run.
    /// </summary>
    [Fact]
    public async Task TranslateBatchAsync_SomeItemsThrottled_KeepsTheOnesThatSucceeded()
    {
        var handler = new FakeHttpMessageHandler(
        [
            (HttpStatusCode.OK, SingleSentenceResponse),
            (HttpStatusCode.TooManyRequests, string.Empty),
        ]);

        var provider = new GoogleTranslateProvider(new HttpClient(handler), new GoogleTranslateProviderOptions(DelayBetweenRequests: TimeSpan.Zero), maxRetries: 0);

        var results = await provider.TranslateBatchAsync(
            [new TranslationRequestItem("1", "Shallow"), new TranslationRequestItem("2", "Deep")], English, PortugueseBrazil);

        Assert.Equal("1", Assert.Single(results).Id);
    }

    [Fact]
    public async Task TranslateBatchAsync_EverythingThrottled_ThrowsWithAnActionableMessage()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.TooManyRequests, string.Empty);
        var provider = new GoogleTranslateProvider(new HttpClient(handler), new GoogleTranslateProviderOptions(DelayBetweenRequests: TimeSpan.Zero), maxRetries: 0);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => provider.TranslateBatchAsync(
            [new TranslationRequestItem("1", "Shallow")], English, PortugueseBrazil));

        Assert.Contains("limita requisições por IP", exception.Message, StringComparison.Ordinal);
    }
}
