using System.Globalization;
using System.Net;
using System.Text.Json;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.OpenAICompatible;

namespace TS4Translator.Providers.Tests;

public class OpenAICompatibleProviderTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    private static string ChatCompletionEnvelope(string assistantContent)
    {
        var envelope = new
        {
            choices = new[]
            {
                new { message = new { role = "assistant", content = assistantContent } },
            },
        };
        return JsonSerializer.Serialize(envelope);
    }

    private static (OpenAICompatibleProvider Provider, FakeHttpMessageHandler Handler) CreateSut(HttpStatusCode statusCode, string responseBody, string? apiKey = null)
    {
        var handler = new FakeHttpMessageHandler(statusCode, responseBody);
        var httpClient = new HttpClient(handler);
        var provider = new OpenAICompatibleProvider(httpClient, new OpenAICompatibleProviderOptions("http://localhost:1234/v1", "test-model", apiKey));
        return (provider, handler);
    }

    [Fact]
    public async Task TranslateBatchAsync_WellFormedResponse_ReturnsAllResultsMatchedById()
    {
        var assistantJson = """[{"id":"1","translation":"Olá."},{"id":"2","translation":"Tchau."}]""";
        var (sut, _) = CreateSut(HttpStatusCode.OK, ChatCompletionEnvelope(assistantJson));
        var items = new[] { new TranslationRequestItem("1", "Hello."), new TranslationRequestItem("2", "Bye.") };

        var results = await sut.TranslateBatchAsync(items, English, PortugueseBrazil);

        Assert.Equal(2, results.Count);
        Assert.Equal("Olá.", results.Single(r => r.Id == "1").TranslatedProtectedText);
        Assert.Equal("Tchau.", results.Single(r => r.Id == "2").TranslatedProtectedText);
    }

    [Fact]
    public async Task TranslateBatchAsync_ResponseWrappedInMarkdownFences_StillParses()
    {
        var assistantContent = "```json\n[{\"id\":\"1\",\"translation\":\"Olá.\"}]\n```";
        var (sut, _) = CreateSut(HttpStatusCode.OK, ChatCompletionEnvelope(assistantContent));
        var items = new[] { new TranslationRequestItem("1", "Hello.") };

        var results = await sut.TranslateBatchAsync(items, English, PortugueseBrazil);

        var result = Assert.Single(results);
        Assert.Equal("Olá.", result.TranslatedProtectedText);
    }

    [Fact]
    public async Task TranslateBatchAsync_TruncatedJson_SalvagesCompleteObjectsOnly()
    {
        // The second object is cut off mid-string, as if the model's output stream was truncated.
        var assistantContent = """[{"id":"1","translation":"Olá."},{"id":"2","translat""";
        var (sut, _) = CreateSut(HttpStatusCode.OK, ChatCompletionEnvelope(assistantContent));
        var items = new[] { new TranslationRequestItem("1", "Hello."), new TranslationRequestItem("2", "Bye.") };

        var results = await sut.TranslateBatchAsync(items, English, PortugueseBrazil);

        var result = Assert.Single(results);
        Assert.Equal("1", result.Id);
        Assert.Equal("Olá.", result.TranslatedProtectedText);
    }

    [Fact]
    public async Task TranslateBatchAsync_CompletelyUnparseableResponse_ReturnsEmptyRatherThanThrowing()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, ChatCompletionEnvelope("I'm sorry, I cannot do that."));
        var items = new[] { new TranslationRequestItem("1", "Hello.") };

        var results = await sut.TranslateBatchAsync(items, English, PortugueseBrazil);

        Assert.Empty(results);
    }

    [Fact]
    public async Task TranslateBatchAsync_EmptyItemsList_ReturnsEmptyWithoutCallingHttp()
    {
        var (sut, handler) = CreateSut(HttpStatusCode.OK, ChatCompletionEnvelope("[]"));

        var results = await sut.TranslateBatchAsync([], English, PortugueseBrazil);

        Assert.Empty(results);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task TranslateBatchAsync_ApiKeyProvided_SendsBearerAuthorizationHeader()
    {
        var (sut, handler) = CreateSut(HttpStatusCode.OK, ChatCompletionEnvelope("[]"), apiKey: "nvapi-secret");
        await sut.TranslateBatchAsync([new TranslationRequestItem("1", "Hello.")], English, PortugueseBrazil);

        Assert.NotNull(handler.LastRequest?.Headers.Authorization);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("nvapi-secret", handler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task TranslateBatchAsync_NoApiKeyProvided_SendsNoAuthorizationHeader()
    {
        var (sut, handler) = CreateSut(HttpStatusCode.OK, ChatCompletionEnvelope("[]"));
        await sut.TranslateBatchAsync([new TranslationRequestItem("1", "Hello.")], English, PortugueseBrazil);

        Assert.Null(handler.LastRequest?.Headers.Authorization);
    }

    [Fact]
    public async Task TranslateBatchAsync_ServerReturnsError_ThrowsHttpRequestException()
    {
        var (sut, _) = CreateSut(HttpStatusCode.InternalServerError, "server error");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            sut.TranslateBatchAsync([new TranslationRequestItem("1", "Hello.")], English, PortugueseBrazil));
    }

    [Fact]
    public async Task TranslateBatchAsync_RequestBody_PreservesTokenPlaceholderVerbatim()
    {
        var (sut, handler) = CreateSut(HttpStatusCode.OK, ChatCompletionEnvelope("[]"));
        await sut.TranslateBatchAsync(
            [new TranslationRequestItem("1", "Ask __TS4_TOKEN_0001__ to stay.")], English, PortugueseBrazil);

        Assert.Contains("__TS4_TOKEN_0001__", handler.LastRequestBody);
    }
}
