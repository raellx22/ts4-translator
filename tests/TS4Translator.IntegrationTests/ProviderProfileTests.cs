using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TS4Translator.Core.Projects;
using TS4Translator.Core.Providers;
using TS4Translator.Core.Security;
using TS4Translator.Persistence;

namespace TS4Translator.IntegrationTests;

public sealed class ProviderProfileTests : IDisposable
{
    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("ts4translator-profiles-tests-").FullName;
    private readonly ServiceProvider _services;
    private readonly IProviderProfileService _sut;
    private readonly IProjectService _projectService;

    public ProviderProfileTests()
    {
        var databasePath = Path.Combine(_tempDirectory, "profiles.db");
        _services = new ServiceCollection()
            .AddPersistenceServices(databasePath)
            .AddSingleton<ISecretStore>(new FakeSecretStore())
            .BuildServiceProvider();

        using var context = _services.GetRequiredService<IDbContextFactory<TranslatorDbContext>>().CreateDbContext();
        context.Database.Migrate();

        _sut = _services.GetRequiredService<IProviderProfileService>();
        _projectService = _services.GetRequiredService<IProjectService>();
    }

    /// <summary>
    /// A pipeline override has a real foreign key to its project - the database rejects a row pointing at
    /// a project that does not exist, which is exactly what should happen.
    /// </summary>
    private async Task<Guid> CreateProjectAsync(string name)
    {
        var project = await _projectService.CreateAsync(
            name, [], CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("pt-BR"));

        return project.Id;
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

    /// <summary>The reason profiles exist at all: two setups of the same engine, side by side.</summary>
    [Fact]
    public async Task SaveAsync_TwoProfilesOfTheSameKind_BothSurviveWithTheirOwnKeys()
    {
        var flash = await _sut.SaveAsync(null, "Gemini Flash", ProviderKind.GoogleGemini, "https://generativelanguage.googleapis.com", "gemini-2.5-flash", 0.0, "key-flash");
        var pro = await _sut.SaveAsync(null, "Gemini Pro", ProviderKind.GoogleGemini, "https://generativelanguage.googleapis.com", "gemini-2.5-pro", 0.0, "key-pro");

        Assert.Equal(2, (await _sut.ListAsync()).Count);
        Assert.Equal("key-flash", await _sut.GetApiKeyAsync(flash.Id));
        Assert.Equal("key-pro", await _sut.GetApiKeyAsync(pro.Id));
    }

    [Fact]
    public async Task SaveAsync_WithAnId_UpdatesInsteadOfCreating()
    {
        var created = await _sut.SaveAsync(null, "LM Studio", ProviderKind.OpenAICompatible, "http://localhost:1234/v1", "qwen", 0.0, null);

        await _sut.SaveAsync(created.Id, "LM Studio (Qwen3.5)", ProviderKind.OpenAICompatible, "http://localhost:1234/v1", "qwen3.5-4b", 0.0, null);

        var only = Assert.Single(await _sut.ListAsync());
        Assert.Equal("LM Studio (Qwen3.5)", only.Name);
        Assert.Equal("qwen3.5-4b", only.Model);
    }

    [Fact]
    public async Task SaveAsync_NullApiKey_KeepsTheStoredOne()
    {
        var created = await _sut.SaveAsync(null, "Gemini", ProviderKind.GoogleGemini, "https://generativelanguage.googleapis.com", "gemini-2.5-flash", 0.0, "secret");

        await _sut.SaveAsync(created.Id, "Gemini renomeado", ProviderKind.GoogleGemini, "https://generativelanguage.googleapis.com", "gemini-2.5-flash", 0.0, apiKey: null);

        Assert.Equal("secret", await _sut.GetApiKeyAsync(created.Id));
        Assert.True((await _sut.GetAsync(created.Id))!.HasApiKey);
    }

    [Fact]
    public async Task SaveAsync_EmptyApiKey_ClearsIt()
    {
        var created = await _sut.SaveAsync(null, "Gemini", ProviderKind.GoogleGemini, "https://generativelanguage.googleapis.com", "gemini-2.5-flash", 0.0, "secret");

        await _sut.SaveAsync(created.Id, "Gemini", ProviderKind.GoogleGemini, "https://generativelanguage.googleapis.com", "gemini-2.5-flash", 0.0, apiKey: "");

        Assert.Null(await _sut.GetApiKeyAsync(created.Id));
        Assert.False((await _sut.GetAsync(created.Id))!.HasApiKey);
    }

    [Fact]
    public async Task DeleteAsync_ProfileHoldingRoles_ClearsThoseRolesToo()
    {
        var argos = await _sut.SaveAsync(null, "Argos", ProviderKind.Argos, "local://argos", "translate-en_pb-1_9", 0.0, null);
        var gemini = await _sut.SaveAsync(null, "Gemini", ProviderKind.GoogleGemini, "https://generativelanguage.googleapis.com", "gemini-2.5-flash", 0.0, null);
        await _sut.SavePipelineSettingsAsync(new TranslationPipelineSettings(argos.Id, gemini.Id, gemini.Id, TieredRoutingEnabled: true, MediumProfileId: null, MediumThreshold: 25, DifficultProfileId: gemini.Id, DifficultThreshold: 35));

        await _sut.DeleteAsync(gemini.Id);

        var settings = await _sut.GetPipelineSettingsAsync();
        Assert.Equal(argos.Id, settings.PrimaryProfileId);
        Assert.Null(settings.RefinementProfileId);
        Assert.Null(settings.ReviewProfileId);
        Assert.Null(settings.DifficultProfileId);

        // A dangling "difficult" engine with routing still on would fail at translation time; the flag
        // has to come down with it.
        Assert.False(settings.TieredRoutingEnabled);
    }

    [Fact]
    public async Task PipelineSettings_RoundTrip()
    {
        var argos = await _sut.SaveAsync(null, "Argos", ProviderKind.Argos, "local://argos", "translate-en_pb-1_9", 0.0, null);
        var claude = await _sut.SaveAsync(null, "Claude", ProviderKind.Anthropic, "https://api.anthropic.com", "claude-sonnet-5", 0.0, "k");

        await _sut.SavePipelineSettingsAsync(new TranslationPipelineSettings(argos.Id, claude.Id, claude.Id, TieredRoutingEnabled: true, MediumProfileId: null, MediumThreshold: 25, DifficultProfileId: claude.Id, DifficultThreshold: 42));
        var settings = await _sut.GetPipelineSettingsAsync();

        Assert.Equal(argos.Id, settings.PrimaryProfileId);
        Assert.Equal(claude.Id, settings.RefinementProfileId);
        Assert.True(settings.TieredRoutingEnabled);
        Assert.Equal(42, settings.DifficultThreshold);
    }

    [Fact]
    public async Task GetPipelineSettingsAsync_NothingSaved_ReturnsDefaults()
    {
        var settings = await _sut.GetPipelineSettingsAsync();

        Assert.Null(settings.PrimaryProfileId);
        Assert.False(settings.TieredRoutingEnabled);
        Assert.Equal(TranslationPipelineSettings.DefaultDifficultThreshold, settings.DifficultThreshold);
    }

    [Fact]
    public async Task SavePipelineSettingsAsync_CalledTwice_KeepsASingleRow()
    {
        var argos = await _sut.SaveAsync(null, "Argos", ProviderKind.Argos, "local://argos", "translate-en_pb-1_9", 0.0, null);

        await _sut.SavePipelineSettingsAsync(new TranslationPipelineSettings(argos.Id));
        await _sut.SavePipelineSettingsAsync(new TranslationPipelineSettings(argos.Id, DifficultThreshold: 50));

        Assert.Equal(50, (await _sut.GetPipelineSettingsAsync()).DifficultThreshold);
    }
    [Fact]
    public async Task SaveAsync_NameAlreadyUsedByAnotherProfile_IsRejected()
    {
        await _sut.SaveAsync(null, "Gemini", ProviderKind.GoogleGemini, "https://generativelanguage.googleapis.com", "gemini-2.5-flash", 0.0, null);

        // Roles are picked by name; two profiles sharing one would silently resolve to the first.
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SaveAsync(
            null, "gemini", ProviderKind.OpenAICompatible, "http://localhost:1234/v1", "x", 0.0, null));
    }

    [Fact]
    public async Task SaveAsync_RenamingAProfileToItsOwnName_IsAllowed()
    {
        var created = await _sut.SaveAsync(null, "Gemini", ProviderKind.GoogleGemini, "https://generativelanguage.googleapis.com", "gemini-2.5-flash", 0.0, null);

        var updated = await _sut.SaveAsync(created.Id, "Gemini", ProviderKind.GoogleGemini, "https://generativelanguage.googleapis.com", "gemini-2.5-pro", 0.0, null);

        Assert.Equal("gemini-2.5-pro", updated.Model);
    }
    [Fact]
    public async Task PipelineSettings_ProjectOverride_WinsOverGlobal()
    {
        var argos = await _sut.SaveAsync(null, "Argos", ProviderKind.Argos, "local://argos", "m", 0.0, null);
        var claude = await _sut.SaveAsync(null, "Claude", ProviderKind.Anthropic, "https://api.anthropic.com", "claude-sonnet-5", 0.0, "k");
        var projectId = await CreateProjectAsync("Projeto de teste");

        await _sut.SavePipelineSettingsAsync(new TranslationPipelineSettings(argos.Id));
        await _sut.SavePipelineSettingsAsync(new TranslationPipelineSettings(claude.Id), projectId);

        Assert.Equal(argos.Id, (await _sut.GetPipelineSettingsAsync()).PrimaryProfileId);
        Assert.Equal(claude.Id, (await _sut.GetPipelineSettingsAsync(projectId)).PrimaryProfileId);
        Assert.True(await _sut.HasPipelineOverrideAsync(projectId));
    }

    [Fact]
    public async Task PipelineSettings_ProjectWithoutOverride_FollowsGlobal()
    {
        var argos = await _sut.SaveAsync(null, "Argos", ProviderKind.Argos, "local://argos", "m", 0.0, null);
        await _sut.SavePipelineSettingsAsync(new TranslationPipelineSettings(argos.Id));

        var projectId = await CreateProjectAsync("Projeto sem override");

        Assert.Equal(argos.Id, (await _sut.GetPipelineSettingsAsync(projectId)).PrimaryProfileId);
        Assert.False(await _sut.HasPipelineOverrideAsync(projectId));
    }

    [Fact]
    public async Task RemovePipelineOverrideAsync_SendsTheProjectBackToGlobal()
    {
        var argos = await _sut.SaveAsync(null, "Argos", ProviderKind.Argos, "local://argos", "m", 0.0, null);
        var claude = await _sut.SaveAsync(null, "Claude", ProviderKind.Anthropic, "https://api.anthropic.com", "claude-sonnet-5", 0.0, "k");
        var projectId = await CreateProjectAsync("Projeto de teste");
        await _sut.SavePipelineSettingsAsync(new TranslationPipelineSettings(argos.Id));
        await _sut.SavePipelineSettingsAsync(new TranslationPipelineSettings(claude.Id), projectId);

        await _sut.RemovePipelineOverrideAsync(projectId);

        Assert.Equal(argos.Id, (await _sut.GetPipelineSettingsAsync(projectId)).PrimaryProfileId);
        Assert.False(await _sut.HasPipelineOverrideAsync(projectId));
    }

    /// <summary>Deleting an engine has to clear it from every scope, not just the global one.</summary>
    [Fact]
    public async Task DeleteAsync_ProfileUsedByAProjectOverride_IsClearedThereToo()
    {
        var argos = await _sut.SaveAsync(null, "Argos", ProviderKind.Argos, "local://argos", "m", 0.0, null);
        var claude = await _sut.SaveAsync(null, "Claude", ProviderKind.Anthropic, "https://api.anthropic.com", "claude-sonnet-5", 0.0, "k");
        var projectId = await CreateProjectAsync("Projeto de teste");
        await _sut.SavePipelineSettingsAsync(new TranslationPipelineSettings(argos.Id));
        await _sut.SavePipelineSettingsAsync(new TranslationPipelineSettings(claude.Id), projectId);

        await _sut.DeleteAsync(claude.Id);

        Assert.Null((await _sut.GetPipelineSettingsAsync(projectId)).PrimaryProfileId);
    }

    [Fact]
    public async Task SavePipelineSettingsAsync_ThresholdsOutOfOrder_AreNormalized()
    {
        var argos = await _sut.SaveAsync(null, "Argos", ProviderKind.Argos, "local://argos", "m", 0.0, null);

        await _sut.SavePipelineSettingsAsync(new TranslationPipelineSettings(
            argos.Id, MediumThreshold: 60, DifficultThreshold: 20));

        var settings = await _sut.GetPipelineSettingsAsync();

        // A difficult band below the medium one would make the middle lane swallow everything.
        Assert.True(settings.DifficultThreshold >= settings.MediumThreshold);
    }
}
