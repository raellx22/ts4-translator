using System.Globalization;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.Argos;

namespace TS4Translator.Providers.Tests;

public class ArgosTranslateProviderTests
{
    private sealed class FakeRuntimeManager : IArgosRuntimeManager
    {
        public string? SourceCode { get; private set; }
        public string? TargetCode { get; private set; }

        public Task<ArgosRuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ArgosRuntimeStatus(true, true, "Ready"));

        public Task<ArgosRuntimeStatus> InstallAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ArgosRuntimeStatus(true, true, "Ready"));

        public Task<IReadOnlyList<TranslationResultItem>> TranslateAsync(IReadOnlyList<TranslationRequestItem> items, string sourceCode, string targetCode, CancellationToken cancellationToken = default)
        {
            SourceCode = sourceCode;
            TargetCode = targetCode;
            return Task.FromResult<IReadOnlyList<TranslationResultItem>>(
                items.Select(item => new TranslationResultItem(item.Id, $"pt:{item.ProtectedText}")).ToList());
        }
    }

    [Fact]
    public async Task TranslateBatchAsync_MapsBrazilianPortugueseToArgosPbCodeAndPreservesIds()
    {
        var runtime = new FakeRuntimeManager();
        var provider = new ArgosTranslateProvider(runtime);

        var results = await provider.TranslateBatchAsync(
            [new TranslationRequestItem("stbl:1", "Hello __TS4_TOKEN_0001__")],
            CultureInfo.GetCultureInfo("en-US"),
            CultureInfo.GetCultureInfo("pt-BR"));

        Assert.Equal("en", runtime.SourceCode);
        Assert.Equal("pb", runtime.TargetCode);
        var result = Assert.Single(results);
        Assert.Equal("stbl:1", result.Id);
        Assert.Contains("__TS4_TOKEN_0001__", result.TranslatedProtectedText);
    }

    [Fact]
    public async Task TranslateBatchAsync_UnsupportedPair_ThrowsBeforeStartingRuntime()
    {
        var provider = new ArgosTranslateProvider(new FakeRuntimeManager());

        await Assert.ThrowsAsync<NotSupportedException>(() => provider.TranslateBatchAsync(
            [new TranslationRequestItem("1", "Olá")],
            CultureInfo.GetCultureInfo("pt-BR"),
            CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public void Capabilities_AreOfflineAndUseLargeCrossStringBatches()
    {
        var capabilities = new ArgosTranslateProvider(new FakeRuntimeManager()).Capabilities;

        Assert.True(capabilities.SupportsLocalExecution);
        Assert.False(capabilities.RequiresApiKey);
        Assert.False(capabilities.SupportsCustomPrompt);
        Assert.False(capabilities.SupportsGlossary);
        Assert.Equal(1, capabilities.RecommendedConcurrency);
        Assert.Equal(256, capabilities.RecommendedBatchItems);
        Assert.Equal(64_000, capabilities.RecommendedBatchCharacters);
    }
}
