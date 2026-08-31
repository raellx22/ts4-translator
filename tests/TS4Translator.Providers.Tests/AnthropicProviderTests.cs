using System.Globalization;
using System.Net;
using System.Text.Json;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.Anthropic;

namespace TS4Translator.Providers.Tests;

public sealed class AnthropicProviderTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    private static AnthropicProviderOptions Options => new("https://api.anthropic.com", "claude-sonnet-5", "test-key");

    /// <summary>Anthropic answers with a list of content blocks; the translation JSON is inside a text block.</summary>
    private static string Envelope(string innerJson) =>
        JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = innerJson } } });

    [Fact]
    public async Task TranslateBatchAsync_ParsesTheTranslationsOutOfTheContentBlock()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK,
            Envelope("""[{"id":"1","translation":"Raso"},{"id":"2","translation":"Quadril médio"}]"""));

        var results = await new AnthropicProvider(new HttpClient(handler), Options).TranslateBatchAsync(
            [new TranslationRequestItem("1", "Shallow"), new TranslationRequestItem("2", "Average Hips")],
            English, PortugueseBrazil);

        Assert.Equal(2, results.Count);
        Assert.Equal("Raso", results.First(r => r.Id == "1").TranslatedProtectedText);
    }

    [Fact]
    public async Task TranslateBatchAsync_SendsApiKeyAndVersionHeaders()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, Envelope("""[{"id":"1","translation":"Raso"}]"""));

        await new AnthropicProvider(new HttpClient(handler), Options).TranslateBatchAsync(
            [new TranslationRequestItem("1", "Shallow")], English, PortugueseBrazil);

        Assert.True(handler.LastRequest!.Headers.Contains("x-api-key"));
        Assert.True(handler.LastRequest.Headers.Contains("anthropic-version"));
    }

    /// <summary>Same guarantee as every other chat provider here: a truncated answer yields what parsed.</summary>
    [Fact]
    public async Task TranslateBatchAsync_TruncatedJson_SalvagesTheCompleteObjects()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK,
            Envelope("""[{"id":"1","translation":"Raso"},{"id":"2","transl"""));

        var results = await new AnthropicProvider(new HttpClient(handler), Options).TranslateBatchAsync(
            [new TranslationRequestItem("1", "Shallow"), new TranslationRequestItem("2", "Average Hips")],
            English, PortugueseBrazil);

        Assert.Equal("1", Assert.Single(results).Id);
    }

    [Fact]
    public async Task TranslateBatchAsync_NullTranslation_IsDropped()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK,
            Envelope("""[{"id":"1","translation":null},{"id":"2","translation":"Quadril médio"}]"""));

        var results = await new AnthropicProvider(new HttpClient(handler), Options).TranslateBatchAsync(
            [new TranslationRequestItem("1", "Shallow"), new TranslationRequestItem("2", "Average Hips")],
            English, PortugueseBrazil);

        Assert.Equal("2", Assert.Single(results).Id);
    }

    [Fact]
    public async Task TranslateBatchAsync_TransientFailure_IsRetried()
    {
        var handler = new FakeHttpMessageHandler(
        [
            (HttpStatusCode.ServiceUnavailable, string.Empty),
            (HttpStatusCode.OK, Envelope("""[{"id":"1","translation":"Raso"}]""")),
        ]);

        var results = await new AnthropicProvider(new HttpClient(handler), Options).TranslateBatchAsync(
            [new TranslationRequestItem("1", "Shallow")], English, PortugueseBrazil);

        Assert.Single(results);
        Assert.True(handler.CallCount > 1);
    }

    [Fact]
    public async Task TranslateBatchAsync_NoItems_MakesNoRequest()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, Envelope("[]"));

        var results = await new AnthropicProvider(new HttpClient(handler), Options).TranslateBatchAsync(
            [], English, PortugueseBrazil);

        Assert.Empty(results);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public void Capabilities_RequireAnApiKey()
        => Assert.True(new AnthropicProvider(new HttpClient(), Options).Capabilities.RequiresApiKey);
}
