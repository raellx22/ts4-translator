using System.Globalization;
using System.Net;
using System.Text.Json;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.DeepL;

namespace TS4Translator.Providers.Tests;

public class DeepLProviderTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly CultureInfo BritishEnglish = CultureInfo.GetCultureInfo("en-GB");
    private static readonly CultureInfo PortuguesePortugal = CultureInfo.GetCultureInfo("pt-PT");

    private static string TranslationsEnvelope(params string[] texts)
    {
        var envelope = new { translations = texts.Select(t => new { text = t }).ToArray() };
        return JsonSerializer.Serialize(envelope);
    }

    private static (DeepLProvider Provider, FakeHttpMessageHandler Handler) CreateSut(HttpStatusCode statusCode, string responseBody, string? apiKey = null)
    {
        var handler = new FakeHttpMessageHandler(statusCode, responseBody);
        var httpClient = new HttpClient(handler);
        var provider = new DeepLProvider(httpClient, new DeepLProviderOptions("https://api-free.deepl.com", apiKey));
        return (provider, handler);
    }

    [Fact]
    public async Task TranslateBatchAsync_WellFormedResponse_ReturnsResultsMatchedByPositionToId()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, TranslationsEnvelope("Olá.", "Tchau."));
        var items = new[] { new TranslationRequestItem("7", "Hello."), new TranslationRequestItem("9", "Bye.") };

        var results = await sut.TranslateBatchAsync(items, English, PortugueseBrazil);

        Assert.Equal(2, results.Count);
        Assert.Equal("Olá.", results.Single(r => r.Id == "7").TranslatedProtectedText);
        Assert.Equal("Tchau.", results.Single(r => r.Id == "9").TranslatedProtectedText);
    }

    [Fact]
    public async Task TranslateBatchAsync_FewerTranslationsThanRequested_OnlyReturnsWhatWasMatched()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, TranslationsEnvelope("Olá."));
        var items = new[] { new TranslationRequestItem("1", "Hello."), new TranslationRequestItem("2", "Bye.") };

        var results = await sut.TranslateBatchAsync(items, English, PortugueseBrazil);

        var result = Assert.Single(results);
        Assert.Equal("1", result.Id);
    }

    [Fact]
    public async Task TranslateBatchAsync_EmptyItemsList_ReturnsEmptyWithoutCallingHttp()
    {
        var (sut, handler) = CreateSut(HttpStatusCode.OK, TranslationsEnvelope());

        var results = await sut.TranslateBatchAsync([], English, PortugueseBrazil);

        Assert.Empty(results);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task TranslateBatchAsync_ApiKeyProvided_SendsDeepLAuthKeyHeader()
    {
        var (sut, handler) = CreateSut(HttpStatusCode.OK, TranslationsEnvelope("Olá."), apiKey: "my-deepl-key");

        await sut.TranslateBatchAsync([new TranslationRequestItem("1", "Hello.")], English, PortugueseBrazil);

        Assert.NotNull(handler.LastRequest?.Headers.Authorization);
        Assert.Equal("DeepL-Auth-Key", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("my-deepl-key", handler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task TranslateBatchAsync_ServerReturnsError_ThrowsHttpRequestException()
    {
        var (sut, _) = CreateSut(HttpStatusCode.Forbidden, "invalid key");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            sut.TranslateBatchAsync([new TranslationRequestItem("1", "Hello.")], English, PortugueseBrazil));
    }

    [Fact]
    public async Task TranslateBatchAsync_RequestBody_PreservesTokenPlaceholderVerbatim()
    {
        var (sut, handler) = CreateSut(HttpStatusCode.OK, TranslationsEnvelope("..."));

        await sut.TranslateBatchAsync([new TranslationRequestItem("1", "Ask __TS4_TOKEN_0001__ to stay.")], English, PortugueseBrazil);

        Assert.Contains("__TS4_TOKEN_0001__", handler.LastRequestBody);
    }

    [Theory]
    [InlineData("en-US", "EN-US")]
    [InlineData("en-GB", "EN-GB")]
    [InlineData("pt-BR", "PT-BR")]
    [InlineData("pt-PT", "PT-PT")]
    [InlineData("fr-FR", "FR")]
    public void ToDeepLTargetLang_MapsCommonLocalesToDeepLCodes(string cultureName, string expected)
    {
        var result = DeepLProvider.ToDeepLTargetLang(CultureInfo.GetCultureInfo(cultureName));

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("en-US", "EN")]
    [InlineData("pt-BR", "PT")]
    [InlineData("fr-FR", "FR")]
    public void ToDeepLSourceLang_UsesJustTheBaseLanguageCode(string cultureName, string expected)
    {
        var result = DeepLProvider.ToDeepLSourceLang(CultureInfo.GetCultureInfo(cultureName));

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task TranslateBatchAsync_RequestBody_UsesRegionSpecificTargetLangForBritishEnglish()
    {
        var (sut, handler) = CreateSut(HttpStatusCode.OK, TranslationsEnvelope("Hi."));

        await sut.TranslateBatchAsync([new TranslationRequestItem("1", "Oi.")], PortugueseBrazil, BritishEnglish);

        Assert.Contains("\"target_lang\":\"EN-GB\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task TranslateBatchAsync_RequestBody_UsesRegionSpecificTargetLangForEuropeanPortuguese()
    {
        var (sut, handler) = CreateSut(HttpStatusCode.OK, TranslationsEnvelope("Olá."));

        await sut.TranslateBatchAsync([new TranslationRequestItem("1", "Hello.")], English, PortuguesePortugal);

        Assert.Contains("\"target_lang\":\"PT-PT\"", handler.LastRequestBody);
    }

    [Fact]
    public void Capabilities_DoesNotClaimGlossaryOrCustomPromptSupport()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, TranslationsEnvelope());

        Assert.False(sut.Capabilities.SupportsGlossary);
        Assert.False(sut.Capabilities.SupportsCustomPrompt);
        Assert.True(sut.Capabilities.RequiresApiKey);
    }
}
