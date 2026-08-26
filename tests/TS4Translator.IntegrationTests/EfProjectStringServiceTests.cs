using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TS4Translator.Core.Packages;
using TS4Translator.Core.Projects;
using TS4Translator.Persistence;

namespace TS4Translator.IntegrationTests;

public sealed class EfProjectStringServiceTests : IAsyncLifetime, IDisposable
{
    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("ts4translator-strings-tests-").FullName;
    private readonly ServiceProvider _services;
    private readonly IProjectStringService _sut;
    private readonly IProjectService _projectService;
    private Guid _projectId;
    private static readonly StblKey Stbl = new(Group: 0, FullInstance: 0x1122UL);
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    public EfProjectStringServiceTests()
    {
        var databasePath = Path.Combine(_tempDirectory, "strings.db");
        _services = new ServiceCollection().AddPersistenceServices(databasePath).BuildServiceProvider();

        using var context = _services.GetRequiredService<IDbContextFactory<TranslatorDbContext>>().CreateDbContext();
        context.Database.Migrate();

        _sut = _services.GetRequiredService<IProjectStringService>();
        _projectService = _services.GetRequiredService<IProjectService>();
    }

    public async Task InitializeAsync()
    {
        // ProjectStringRecord has a foreign key to Project — every test needs a real project row.
        var project = await _projectService.CreateAsync("Test Project", [], English, PortugueseBrazil);
        _projectId = project.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

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
    public async Task ReplaceStringsAsync_ThenGetStringsAsync_RoundTripsAsPending()
    {
        await _sut.ReplaceStringsAsync(_projectId, Stbl, [(1u, "Hello."), (2u, "Bye.")]);

        var strings = await _sut.GetStringsAsync(_projectId);

        Assert.Equal(2, strings.Count);
        Assert.All(strings, s => Assert.Equal(ProjectStringStatus.Pending, s.Status));
        Assert.All(strings, s => Assert.Equal(Stbl, s.SourceStbl));
    }

    [Fact]
    public async Task UpdateTranslationAsync_SetsTranslationStatusAndIssueSummary()
    {
        await _sut.ReplaceStringsAsync(_projectId, Stbl, [(1u, "Hello.")]);
        var entry = (await _sut.GetStringsAsync(_projectId))[0];

        await _sut.UpdateTranslationAsync(entry.Id, "Olá.", ProjectStringStatus.Translated, issueSummary: null);
        var updated = (await _sut.GetStringsAsync(_projectId))[0];

        Assert.Equal("Olá.", updated.TranslatedText);
        Assert.Equal(ProjectStringStatus.Translated, updated.Status);
        Assert.Null(updated.IssueSummary);
    }

    [Fact]
    public async Task GetPendingStringsAsync_ExcludesAlreadyTranslatedEntries()
    {
        await _sut.ReplaceStringsAsync(_projectId, Stbl, [(1u, "Hello."), (2u, "Bye.")]);
        var entries = await _sut.GetStringsAsync(_projectId);
        await _sut.UpdateTranslationAsync(entries[0].Id, "Olá.", ProjectStringStatus.Translated, null);

        var pending = await _sut.GetPendingStringsAsync(_projectId);

        var only = Assert.Single(pending);
        Assert.Equal(entries[1].Id, only.Id);
    }

    [Fact]
    public async Task ReplaceStringsAsync_CalledAgainWithSameHash_PreservesExistingTranslation()
    {
        await _sut.ReplaceStringsAsync(_projectId, Stbl, [(1u, "Ask about their day.")]);
        var first = (await _sut.GetStringsAsync(_projectId))[0];
        await _sut.UpdateTranslationAsync(first.Id, "Perguntar sobre o dia.", ProjectStringStatus.Translated, null);

        // Simulates re-importing the same mod version: same key hash, unchanged text.
        await _sut.ReplaceStringsAsync(_projectId, Stbl, [(1u, "Ask about their day.")]);
        var afterReimport = (await _sut.GetStringsAsync(_projectId))[0];

        Assert.Equal("Perguntar sobre o dia.", afterReimport.TranslatedText);
        Assert.Equal(ProjectStringStatus.Translated, afterReimport.Status);
    }

    [Fact]
    public async Task ReplaceStringsAsync_HashNoLongerPresent_RemovesTheEntry()
    {
        await _sut.ReplaceStringsAsync(_projectId, Stbl, [(1u, "A"), (2u, "B")]);

        await _sut.ReplaceStringsAsync(_projectId, Stbl, [(1u, "A")]);
        var strings = await _sut.GetStringsAsync(_projectId);

        var only = Assert.Single(strings);
        Assert.Equal(1u, only.KeyHash);
    }

    [Fact]
    public async Task ReplaceStringsAsync_DifferentProjects_DoNotCollide()
    {
        var otherProject = await _projectService.CreateAsync("Other Project", [], English, PortugueseBrazil);
        var otherProjectId = otherProject.Id;
        await _sut.ReplaceStringsAsync(_projectId, Stbl, [(1u, "Project A string")]);
        await _sut.ReplaceStringsAsync(otherProjectId, Stbl, [(1u, "Project B string")]);

        var a = await _sut.GetStringsAsync(_projectId);
        var b = await _sut.GetStringsAsync(otherProjectId);

        Assert.Equal("Project A string", Assert.Single(a).OriginalText);
        Assert.Equal("Project B string", Assert.Single(b).OriginalText);
    }
}
