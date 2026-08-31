using System.Globalization;
using System.Net;
using System.Text.Json;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.GoogleGemini;

namespace TS4Translator.Providers.Tests;

public class GoogleGeminiProviderTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    private static string GenerateContentEnvelope(string modelText)
    {
        var envelope = new
        {
            candidates = new[]
            {
                new { content = new { parts = new[] { new { text = modelText } } } },
            },
        };
        return JsonSerializer.Serialize(envelope);
    }

    private static (GoogleGeminiProvider Provider, FakeHttpMessageHandler Handler) CreateSut(HttpStatusCode statusCode, string responseBody, string? apiKey = null, int maxRetries = 3, string model = "gemini-2.5-flash")
    {
        var handler = new FakeHttpMessageHandler(statusCode, responseBody);
        var httpClient = new HttpClient(handler);
        var provider = new GoogleGeminiProvider(httpClient, new GoogleGeminiProviderOptions("https://generativelanguage.googleapis.com", model, apiKey), maxRetries);
        return (provider, handler);
    }

    [Fact]
    public async Task TranslateBatchAsync_WellFormedResponse_ReturnsAllResultsMatchedById()
    {
        var modelJson = """[{"id":"1","translation":"Olá."},{"id":"2","translation":"Tchau."}]""";
        var (sut, _) = CreateSut(HttpStatusCode.OK, GenerateContentEnvelope(modelJson));
        var items = new[] { new TranslationRequestItem("1", "Hello."), new TranslationRequestItem("2", "Bye.") };

        var results = await sut.TranslateBatchAsync(items, English, PortugueseBrazil);

        Assert.Equal(2, results.Count);
        Assert.Equal("Olá.", results.Single(r => r.Id == "1").TranslatedProtectedText);
        Assert.Equal("Tchau.", results.Single(r => r.Id == "2").TranslatedProtectedText);
    }

    [Fact]
    public async Task TranslateBatchAsync_StructuredResponse_UsesSchemaOutputCapAndMinimalThinkingForGemini3()
    {
        var modelJson = """{"translations":{"1":"Olá.","2":"Tchau."}}""";
        var (sut, handler) = CreateSut(HttpStatusCode.OK, GenerateContentEnvelope(modelJson), model: "gemini-3.5-flash-lite");

        var results = await sut.TranslateBatchAsync(
            [new TranslationRequestItem("1", "Hello."), new TranslationRequestItem("2", "Bye.")],
            English,
            PortugueseBrazil);

        Assert.Equal(2, results.Count);
        Assert.Contains("\"responseMimeType\":\"application/json\"", handler.LastRequestBody);
        Assert.DoesNotContain("additionalProperties", handler.LastRequestBody);
        Assert.Contains("\"required\":[\"1\",\"2\"]", handler.LastRequestBody);
        Assert.Contains("\"maxOutputTokens\":256", handler.LastRequestBody);
        Assert.Contains("\"thinkingLevel\":\"minimal\"", handler.LastRequestBody);
        Assert.DoesNotContain("thinkingBudget", handler.LastRequestBody);
    }

    [Fact]
    public async Task TranslateBatchAsync_Gemini25Flash_DisablesThinkingWithLegacyBudget()
    {
        var (sut, handler) = CreateSut(HttpStatusCode.OK, GenerateContentEnvelope("[]"));

        await sut.TranslateBatchAsync([new TranslationRequestItem("1", "Hello.")], English, PortugueseBrazil);

        Assert.Contains("\"thinkingBudget\":0", handler.LastRequestBody);
        Assert.DoesNotContain("thinkingLevel", handler.LastRequestBody);
    }

    [Fact]
    public async Task TranslateBatchAsync_ResponseWrappedInMarkdownFences_StillParses()
    {
        var modelText = "```json\n[{\"id\":\"1\",\"translation\":\"Olá.\"}]\n```";
        var (sut, _) = CreateSut(HttpStatusCode.OK, GenerateContentEnvelope(modelText));
        var items = new[] { new TranslationRequestItem("1", "Hello.") };

        var results = await sut.TranslateBatchAsync(items, English, PortugueseBrazil);

        var result = Assert.Single(results);
        Assert.Equal("Olá.", result.TranslatedProtectedText);
    }

    [Fact]
    public async Task TranslateBatchAsync_TruncatedJson_SalvagesCompleteObjectsOnly()
    {
        var modelText = """[{"id":"1","translation":"Olá."},{"id":"2","translat""";
        var (sut, _) = CreateSut(HttpStatusCode.OK, GenerateContentEnvelope(modelText));
        var items = new[] { new TranslationRequestItem("1", "Hello."), new TranslationRequestItem("2", "Bye.") };

        var results = await sut.TranslateBatchAsync(items, English, PortugueseBrazil);

        var result = Assert.Single(results);
        Assert.Equal("1", result.Id);
    }

    [Fact]
    public async Task TranslateBatchAsync_ResponseHasNullTranslationForOneItem_DropsItRatherThanThrowing()
    {
        var modelJson = """[{"id":"1","translation":"Olá."},{"id":"2","translation":null}]""";
        var (sut, _) = CreateSut(HttpStatusCode.OK, GenerateContentEnvelope(modelJson));
        var items = new[] { new TranslationRequestItem("1", "Hello."), new TranslationRequestItem("2", "Bye.") };

        var results = await sut.TranslateBatchAsync(items, English, PortugueseBrazil);

        var result = Assert.Single(results);
        Assert.Equal("1", result.Id);
    }

    [Fact]
    public async Task TranslateBatchAsync_EmptyItemsList_ReturnsEmptyWithoutCallingHttp()
    {
        var (sut, handler) = CreateSut(HttpStatusCode.OK, GenerateContentEnvelope("[]"));

        var results = await sut.TranslateBatchAsync([], English, PortugueseBrazil);

        Assert.Empty(results);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task TranslateBatchAsync_ApiKeyProvided_SendsGoogApiKeyHeader()
    {
        var (sut, handler) = CreateSut(HttpStatusCode.OK, GenerateContentEnvelope("[]"), apiKey: "my-gemini-key");

        await sut.TranslateBatchAsync([new TranslationRequestItem("1", "Hello.")], English, PortugueseBrazil);

        Assert.True(handler.LastRequest?.Headers.Contains("x-goog-api-key"));
        Assert.Equal("my-gemini-key", handler.LastRequest!.Headers.GetValues("x-goog-api-key").Single());
    }

    [Fact]
    public async Task TranslateBatchAsync_ServerReturnsError_ThrowsHttpRequestException()
    {
        // maxRetries: 0 keeps this deterministic and fast - retry behavior itself is covered by
        // HttpRetryTests and TranslateBatchAsync_TransientServerError_RetriesAndEventuallySucceeds below.
        var (sut, _) = CreateSut(HttpStatusCode.InternalServerError, "server error", maxRetries: 0);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            sut.TranslateBatchAsync([new TranslationRequestItem("1", "Hello.")], English, PortugueseBrazil));
    }

    [Fact]
    public async Task TranslateBatchAsync_TransientServerError_RetriesAndEventuallySucceeds()
    {
        var handler = new FakeHttpMessageHandler([
            (HttpStatusCode.ServiceUnavailable, "overloaded"),
            (HttpStatusCode.OK, GenerateContentEnvelope("""[{"id":"1","translation":"Olá."}]""")),
        ]);
        var httpClient = new HttpClient(handler);
        var sut = new GoogleGeminiProvider(httpClient, new GoogleGeminiProviderOptions("https://generativelanguage.googleapis.com", "gemini-2.5-flash", null), maxRetries: 2);

        var results = await sut.TranslateBatchAsync([new TranslationRequestItem("1", "Hello.")], English, PortugueseBrazil);

        Assert.Equal(2, handler.CallCount);
        var result = Assert.Single(results);
        Assert.Equal("Olá.", result.TranslatedProtectedText);
    }

    [Fact]
    public async Task TranslateBatchAsync_RequestBody_PreservesTokenPlaceholderVerbatim()
    {
        var (sut, handler) = CreateSut(HttpStatusCode.OK, GenerateContentEnvelope("[]"));

        await sut.TranslateBatchAsync([new TranslationRequestItem("1", "Ask __TS4_TOKEN_0001__ to stay.")], English, PortugueseBrazil);

        Assert.Contains("__TS4_TOKEN_0001__", handler.LastRequestBody);
    }

    [Fact]
    public async Task TranslateBatchAsync_GlossaryTermProvided_IncludesItInSystemInstruction()
    {
        var (sut, handler) = CreateSut(HttpStatusCode.OK, GenerateContentEnvelope("[]"));
        var glossary = new[] { new GlossaryTerm(1, "Grim Reaper", "Ceifador", NeverTranslate: false, CaseSensitive: false, Notes: null, ProjectId: null) };

        await sut.TranslateBatchAsync([new TranslationRequestItem("1", "Talk to the Grim Reaper.")], English, PortugueseBrazil, glossary);

        Assert.Contains("Grim Reaper", handler.LastRequestBody);
        Assert.Contains("Ceifador", handler.LastRequestBody);
    }

    [Fact]
    public async Task TranslateBatchAsync_RequestBody_UsesSystemInstructionField()
    {
        var (sut, handler) = CreateSut(HttpStatusCode.OK, GenerateContentEnvelope("[]"));

        await sut.TranslateBatchAsync([new TranslationRequestItem("1", "Hello.")], English, PortugueseBrazil);

        Assert.Contains("systemInstruction", handler.LastRequestBody);
    }

    [Fact]
    public void Capabilities_ReportsGlossaryAndCustomPromptSupport()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, GenerateContentEnvelope("[]"));

        Assert.True(sut.Capabilities.SupportsGlossary);
        Assert.True(sut.Capabilities.SupportsCustomPrompt);
        Assert.True(sut.Capabilities.RequiresApiKey);
        Assert.Equal(4, sut.Capabilities.RecommendedConcurrency);
        Assert.Equal(80, sut.Capabilities.RecommendedBatchItems);
        Assert.Equal(6400, sut.Capabilities.RecommendedBatchCharacters);
    }

    [Fact]
    public void Name_IncludesTheConfiguredModel()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, GenerateContentEnvelope("[]"));

        Assert.Contains("gemini-2.5-flash", sut.Name);
    }
}
