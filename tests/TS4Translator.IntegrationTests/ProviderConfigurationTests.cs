using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TS4Translator.Core.Providers;
using TS4Translator.Core.Security;
using TS4Translator.Infrastructure;
using TS4Translator.Persistence;

namespace TS4Translator.IntegrationTests;

public sealed class ProviderConfigurationTests : IDisposable
{
    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("ts4translator-providercfg-tests-").FullName;
    private readonly ServiceProvider _services;

    public ProviderConfigurationTests()
    {
        var databasePath = Path.Combine(_tempDirectory, "cfg.db");
        _services = new ServiceCollection()
            .AddPersistenceServices(databasePath)
            .AddSingleton<ISecretStore>(new FakeSecretStore())
            .BuildServiceProvider();

        using var context = _services.GetRequiredService<IDbContextFactory<TranslatorDbContext>>().CreateDbContext();
        context.Database.Migrate();
    }

    public void Dispose()
    {
        _services.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task GetActiveAsync_NothingSaved_ReturnsNull()
    {
        var sut = _services.GetRequiredService<IProviderConfigurationService>();

        var result = await sut.GetActiveAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_ThenGetActiveAsync_RoundTripsWithoutExposingTheApiKey()
    {
        var sut = _services.GetRequiredService<IProviderConfigurationService>();

        await sut.SaveAsync(ProviderKind.OpenAICompatible, "LM Studio", "http://localhost:1234/v1", "local-model", 0.0, apiKey: "super-secret");
        var active = await sut.GetActiveAsync();

        Assert.NotNull(active);
        Assert.Equal(ProviderKind.OpenAICompatible, active.Kind);
        Assert.Equal("LM Studio", active.Name);
        Assert.Equal("http://localhost:1234/v1", active.Endpoint);
        Assert.True(active.HasApiKey);
        Assert.Equal("super-secret", sut.GetApiKey());
    }

    [Fact]
    public async Task SaveAsync_CalledAgainWithNullApiKey_KeepsThePreviousKey()
    {
        var sut = _services.GetRequiredService<IProviderConfigurationService>();
        await sut.SaveAsync(ProviderKind.OpenAICompatible, "LM Studio", "http://localhost:1234/v1", "local-model", 0.0, apiKey: "first-key");

        await sut.SaveAsync(ProviderKind.OpenAICompatible, "LM Studio", "http://localhost:1234/v1", "renamed-model", 0.0, apiKey: null);

        var active = await sut.GetActiveAsync();
        Assert.Equal("renamed-model", active!.Model);
        Assert.Equal("first-key", sut.GetApiKey());
    }

    [Fact]
    public async Task SaveAsync_CalledWithEmptyApiKey_ClearsTheStoredKey()
    {
        var sut = _services.GetRequiredService<IProviderConfigurationService>();
        await sut.SaveAsync(ProviderKind.OpenAICompatible, "LM Studio", "http://localhost:1234/v1", "local-model", 0.0, apiKey: "first-key");

        await sut.SaveAsync(ProviderKind.OpenAICompatible, "LM Studio", "http://localhost:1234/v1", "local-model", 0.0, apiKey: "");

        var active = await sut.GetActiveAsync();
        Assert.False(active!.HasApiKey);
        Assert.Null(sut.GetApiKey());
    }

    [Fact]
    public async Task SaveAsync_DeepLWithNullModel_RoundTripsWithNullModel()
    {
        var sut = _services.GetRequiredService<IProviderConfigurationService>();

        await sut.SaveAsync(ProviderKind.DeepL, "DeepL", "https://api-free.deepl.com", model: null, 0.0, apiKey: "deepl-key");
        var active = await sut.GetActiveAsync();

        Assert.Equal(ProviderKind.DeepL, active!.Kind);
        Assert.Null(active.Model);
    }

    [Fact]
    public async Task SaveAsync_SwitchingProviderKind_DoesNotLeakTheOldKindsApiKey()
    {
        var sut = _services.GetRequiredService<IProviderConfigurationService>();
        await sut.SaveAsync(ProviderKind.OpenAICompatible, "LM Studio", "http://localhost:1234/v1", "local-model", 0.0, apiKey: "openai-key");

        // Switching to DeepL without providing a key yet - the previous kind's key must not surface as
        // if it belonged to the new configuration (each kind keeps its own secret slot).
        await sut.SaveAsync(ProviderKind.DeepL, "DeepL", "https://api-free.deepl.com", model: null, 0.0, apiKey: null);

        var active = await sut.GetActiveAsync();
        Assert.False(active!.HasApiKey);
        Assert.Null(sut.GetApiKey());
    }

    [Fact]
    public async Task SaveAsync_SwitchingBackToAPreviouslyConfiguredKind_RestoresThatKindsOwnKey()
    {
        var sut = _services.GetRequiredService<IProviderConfigurationService>();
        await sut.SaveAsync(ProviderKind.OpenAICompatible, "LM Studio", "http://localhost:1234/v1", "local-model", 0.0, apiKey: "openai-key");
        await sut.SaveAsync(ProviderKind.DeepL, "DeepL", "https://api-free.deepl.com", model: null, 0.0, apiKey: "deepl-key");

        await sut.SaveAsync(ProviderKind.OpenAICompatible, "LM Studio", "http://localhost:1234/v1", "local-model", 0.0, apiKey: null);

        Assert.Equal("openai-key", sut.GetApiKey());
    }

    private sealed class FakeSecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _secrets = [];

        public void Save(string key, string secret) => _secrets[key] = secret;

        public string? Load(string key) => _secrets.GetValueOrDefault(key);

        public void Delete(string key) => _secrets.Remove(key);
    }
}
