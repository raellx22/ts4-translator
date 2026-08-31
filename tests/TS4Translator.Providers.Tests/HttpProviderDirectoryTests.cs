using System.Net;
using TS4Translator.Core.Providers;

namespace TS4Translator.Providers.Tests;

public sealed class HttpProviderDirectoryTests
{
    private sealed class SingleProfileService(ProviderProfile profile, string? apiKey) : IProviderProfileService
    {
        public Task<IReadOnlyList<ProviderProfile>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderProfile>>([profile]);

        public Task<ProviderProfile?> GetAsync(int profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProviderProfile?>(profileId == profile.Id ? profile : null);

        public Task<string?> GetApiKeyAsync(int profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(apiKey);

        public Task<ProviderProfile> SaveAsync(int? profileId, string name, ProviderKind kind, string endpoint, string? model, double temperature, string? key, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteAsync(int profileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<TranslationPipelineSettings> GetPipelineSettingsAsync(Guid? projectId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TranslationPipelineSettings());

        public Task SavePipelineSettingsAsync(TranslationPipelineSettings settings, Guid? projectId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> HasPipelineOverrideAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task RemovePipelineOverrideAsync(Guid projectId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static (HttpProviderDirectory Directory, FakeHttpMessageHandler Handler) Create(
        ProviderKind kind, string responseBody, string endpoint = "https://example.test", HttpStatusCode status = HttpStatusCode.OK)
    {
        var profile = new ProviderProfile(1, "perfil", kind, endpoint, "m", 0.0, HasApiKey: true);
        var handler = new FakeHttpMessageHandler(status, responseBody);
        return (new HttpProviderDirectory(new SingleProfileService(profile, "test-key"), new HttpClient(handler)), handler);
    }

    [Theory]
    [InlineData(ProviderKind.OpenAI, true)]
    [InlineData(ProviderKind.OpenAICompatible, true)]
    [InlineData(ProviderKind.GoogleGemini, true)]
    [InlineData(ProviderKind.Anthropic, true)]
    [InlineData(ProviderKind.DeepL, false)]
    [InlineData(ProviderKind.Argos, false)]
    [InlineData(ProviderKind.GoogleTranslate, false)]
    public void SupportsModelListing_MatchesWhatTheProvidersActuallyOffer(ProviderKind kind, bool expected)
    {
        var (directory, _) = Create(kind, "{}");

        Assert.Equal(expected, directory.SupportsModelListing(kind));
    }

    /// <summary>Only DeepL reports quota to an ordinary API key; the others keep it in a billing console.</summary>
    [Theory]
    [InlineData(ProviderKind.DeepL, true)]
    [InlineData(ProviderKind.GoogleGemini, false)]
    [InlineData(ProviderKind.OpenAI, false)]
    [InlineData(ProviderKind.Anthropic, false)]
    public void SupportsUsage_OnlyDeepL(ProviderKind kind, bool expected)
    {
        var (directory, _) = Create(kind, "{}");

        Assert.Equal(expected, directory.SupportsUsage(kind));
    }

    [Fact]
    public async Task ListModelsAsync_OpenAiShape_ReadsTheDataArray()
    {
        var (directory, handler) = Create(ProviderKind.OpenAI, """{"data":[{"id":"gpt-4.1-mini"},{"id":"gpt-4.1"}]}""");

        var models = await directory.ListModelsAsync(1);

        Assert.Equal(2, models.Count);
        Assert.Contains(models, model => model.Id == "gpt-4.1-mini");
        Assert.Contains("/models", handler.LastRequest!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    /// <summary>Gemini names models "models/gemini-2.5-flash"; the id the app must send drops that prefix.</summary>
    [Fact]
    public async Task ListModelsAsync_Gemini_StripsThePrefixAndKeepsOnlyGenerativeModels()
    {
        var (directory, _) = Create(ProviderKind.GoogleGemini, """
            {"models":[
              {"name":"models/gemini-2.5-flash","displayName":"Gemini 2.5 Flash","supportedGenerationMethods":["generateContent"]},
              {"name":"models/text-embedding-004","displayName":"Embeddings","supportedGenerationMethods":["embedContent"]}
            ]}
            """);

        var models = await directory.ListModelsAsync(1);

        var only = Assert.Single(models);
        Assert.Equal("gemini-2.5-flash", only.Id);
        Assert.Equal("Gemini 2.5 Flash", only.DisplayName);
    }

    [Fact]
    public async Task ListModelsAsync_Anthropic_SendsTheVersionHeaderAndReadsDisplayNames()
    {
        var (directory, handler) = Create(ProviderKind.Anthropic, """{"data":[{"id":"claude-sonnet-5","display_name":"Claude Sonnet 5"}]}""");

        var models = await directory.ListModelsAsync(1);

        Assert.Equal("Claude Sonnet 5", Assert.Single(models).DisplayName);
        Assert.True(handler.LastRequest!.Headers.Contains("anthropic-version"));
        Assert.True(handler.LastRequest.Headers.Contains("x-api-key"));
    }

    [Fact]
    public async Task ListModelsAsync_UnsupportedKind_ReturnsEmptyWithoutCalling()
    {
        var (directory, handler) = Create(ProviderKind.Argos, "{}");

        Assert.Empty(await directory.ListModelsAsync(1));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ListModelsAsync_UnexpectedShape_ReturnsEmptyRatherThanThrowing()
    {
        var (directory, _) = Create(ProviderKind.OpenAI, """{"something":"else"}""");

        Assert.Empty(await directory.ListModelsAsync(1));
    }

    [Fact]
    public async Task ListModelsAsync_ServerRejectsTheKey_Throws()
    {
        var (directory, _) = Create(ProviderKind.OpenAI, "{}", status: HttpStatusCode.Unauthorized);

        // Surfaced, not swallowed: an empty list would read as "this account has no models".
        await Assert.ThrowsAsync<HttpRequestException>(() => directory.ListModelsAsync(1));
    }

    [Fact]
    public async Task GetUsageAsync_DeepL_ReportsCharactersAndPercentage()
    {
        var (directory, _) = Create(ProviderKind.DeepL, """{"character_count":250000,"character_limit":500000}""");

        var usage = await directory.GetUsageAsync(1);

        Assert.NotNull(usage);
        Assert.Contains("50", usage.Description, StringComparison.Ordinal);
        Assert.False(usage.IsNearLimit);
    }

    [Fact]
    public async Task GetUsageAsync_DeepLNearTheCap_FlagsIt()
    {
        var (directory, _) = Create(ProviderKind.DeepL, """{"character_count":450000,"character_limit":500000}""");

        Assert.True((await directory.GetUsageAsync(1))!.IsNearLimit);
    }

    [Fact]
    public async Task GetUsageAsync_ProviderWithNoQuotaEndpoint_ReturnsNull()
    {
        var (directory, handler) = Create(ProviderKind.GoogleGemini, "{}");

        Assert.Null(await directory.GetUsageAsync(1));
        Assert.Equal(0, handler.CallCount);
    }
}
