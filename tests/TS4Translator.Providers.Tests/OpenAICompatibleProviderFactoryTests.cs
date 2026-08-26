using TS4Translator.Core.Providers;
using TS4Translator.Providers.OpenAICompatible;

namespace TS4Translator.Providers.Tests;

public class OpenAICompatibleProviderFactoryTests
{
    private sealed class FakeProviderConfigurationService(ProviderConfiguration? active, string? apiKey) : IProviderConfigurationService
    {
        public Task<ProviderConfiguration?> GetActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult(active);

        public Task<ProviderConfiguration> SaveAsync(string name, string endpoint, string model, double temperature, string? apiKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public string? GetApiKey() => apiKey;
    }

    [Fact]
    public async Task CreateActiveProviderAsync_NoConfigurationSaved_ReturnsNull()
    {
        var factory = new OpenAICompatibleProviderFactory(new FakeProviderConfigurationService(null, null), new HttpClient());

        var provider = await factory.CreateActiveProviderAsync();

        Assert.Null(provider);
    }

    [Fact]
    public async Task CreateActiveProviderAsync_ConfigurationSaved_ReturnsProviderWithMatchingModel()
    {
        var config = new ProviderConfiguration(1, "LM Studio", "http://localhost:1234/v1", "local-model", 0.0, HasApiKey: false);
        var factory = new OpenAICompatibleProviderFactory(new FakeProviderConfigurationService(config, null), new HttpClient());

        var provider = await factory.CreateActiveProviderAsync();

        Assert.NotNull(provider);
        Assert.Contains("local-model", provider.Name);
    }
}
