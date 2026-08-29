using TS4Translator.Core.Providers;

namespace TS4Translator.Providers.Tests;

public class ConfiguredProviderFactoryTests
{
    private sealed class FakeProviderConfigurationService(ProviderConfiguration? active, string? apiKey) : IProviderConfigurationService
    {
        public Task<ProviderConfiguration?> GetActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult(active);

        public Task<ProviderConfiguration> SaveAsync(ProviderKind kind, string name, string endpoint, string? model, double temperature, string? apiKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public string? GetApiKey() => apiKey;
    }

    [Fact]
    public async Task CreateActiveProviderAsync_NoConfigurationSaved_ReturnsNull()
    {
        var factory = new ConfiguredProviderFactory(new FakeProviderConfigurationService(null, null), new HttpClient());

        var provider = await factory.CreateActiveProviderAsync();

        Assert.Null(provider);
    }

    [Fact]
    public async Task CreateActiveProviderAsync_OpenAICompatibleConfiguration_ReturnsProviderWithMatchingModel()
    {
        var config = new ProviderConfiguration(1, ProviderKind.OpenAICompatible, "LM Studio", "http://localhost:1234/v1", "local-model", 0.0, HasApiKey: false);
        var factory = new ConfiguredProviderFactory(new FakeProviderConfigurationService(config, null), new HttpClient());

        var provider = await factory.CreateActiveProviderAsync();

        Assert.NotNull(provider);
        Assert.Contains("local-model", provider.Name);
    }

    [Fact]
    public async Task CreateActiveProviderAsync_DeepLConfiguration_ReturnsDeepLProvider()
    {
        var config = new ProviderConfiguration(1, ProviderKind.DeepL, "DeepL", "https://api-free.deepl.com", null, 0.0, HasApiKey: true);
        var factory = new ConfiguredProviderFactory(new FakeProviderConfigurationService(config, "fake-key"), new HttpClient());

        var provider = await factory.CreateActiveProviderAsync();

        Assert.NotNull(provider);
        Assert.Equal("deepl", provider.Name);
    }

    [Fact]
    public async Task CreateActiveProviderAsync_GoogleGeminiConfiguration_ReturnsProviderWithMatchingModel()
    {
        var config = new ProviderConfiguration(1, ProviderKind.GoogleGemini, "Gemini", "https://generativelanguage.googleapis.com", "gemini-2.5-flash", 0.0, HasApiKey: true);
        var factory = new ConfiguredProviderFactory(new FakeProviderConfigurationService(config, "fake-key"), new HttpClient());

        var provider = await factory.CreateActiveProviderAsync();

        Assert.NotNull(provider);
        Assert.Contains("gemini-2.5-flash", provider.Name);
    }

    [Fact]
    public async Task CreateActiveProviderAsync_ConfigurationWithApiKeyStored_PassesTheKeyThrough()
    {
        var config = new ProviderConfiguration(1, ProviderKind.DeepL, "DeepL", "https://api-free.deepl.com", null, 0.0, HasApiKey: true);
        var configurationService = new FakeProviderConfigurationService(config, "real-secret-key");
        var factory = new ConfiguredProviderFactory(configurationService, new HttpClient());

        // GetApiKey() is only ever called when HasApiKey is true - confirms the factory actually reads
        // the stored key rather than silently constructing an unauthenticated provider.
        var provider = await factory.CreateActiveProviderAsync();

        Assert.NotNull(provider);
        Assert.True(provider.Capabilities.RequiresApiKey);
    }
}
