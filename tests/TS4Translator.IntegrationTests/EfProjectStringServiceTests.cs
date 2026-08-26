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

    [Fact]
    public async Task ReplaceStringsAsync_FirstImport_ReportsEverythingAsNew()
    {
        var diff = await _sut.ReplaceStringsAsync(_projectId, Stbl, [(1u, "A"), (2u, "B"), (3u, "C")]);

        Assert.Equal(3, diff.NewCount);
        Assert.Equal(0, diff.ChangedCount);
        Assert.Equal(0, diff.UnchangedCount);
        Assert.Equal(0, diff.RemovedCount);
        Assert.Equal(3, diff.TotalAfterImport);
    }

    [Fact]
    public async Task ReplaceStringsAsync_ReimportWithMixOfNewChangedUnchangedRemoved_ReportsEachCategory()
    {
        await _sut.ReplaceStringsAsync(_projectId, Stbl, [(1u, "Unchanged"), (2u, "Old text"), (3u, "Will be removed")]);

        var diff = await _sut.ReplaceStringsAsync(_projectId, Stbl, [(1u, "Unchanged"), (2u, "New text"), (4u, "Brand new")]);

        Assert.Equal(1, diff.NewCount);
        Assert.Equal(1, diff.ChangedCount);
        Assert.Equal(1, diff.UnchangedCount);
        Assert.Equal(1, diff.RemovedCount);
    }

    [Fact]
    public async Task ReplaceStringsAsync_TextChangedOnATranslatedEntry_FlagsItForReviewButKeepsTheTranslation()
    {
        await _sut.ReplaceStringsAsync(_projectId, Stbl, [(1u, "Ask about their old job.")]);
        var entry = (await _sut.GetStringsAsync(_projectId))[0];
        await _sut.UpdateTranslationAsync(entry.Id, "Perguntar sobre o antigo emprego.", ProjectStringStatus.Translated, null);

        await _sut.ReplaceStringsAsync(_projectId, Stbl, [(1u, "Ask about their new job.")]);
        var afterChange = (await _sut.GetStringsAsync(_projectId))[0];

        Assert.Equal("Ask about their new job.", afterChange.OriginalText);
        Assert.Equal("Perguntar sobre o antigo emprego.", afterChange.TranslatedText);
        Assert.Equal(ProjectStringStatus.NeedsReview, afterChange.Status);
        Assert.NotNull(afterChange.IssueSummary);
    }

    [Fact]
    public async Task UpdateTranslationAsync_OverwritingAPreviousTranslation_RecordsARevision()
    {
        await _sut.ReplaceStringsAsync(_projectId, Stbl, [(1u, "Hello.")]);
        var entry = (await _sut.GetStringsAsync(_projectId))[0];
        await _sut.UpdateTranslationAsync(entry.Id, "Primeira versão.", ProjectStringStatus.Translated, null);

        await _sut.UpdateTranslationAsync(entry.Id, "Segunda versão.", ProjectStringStatus.ManuallyEdited, null);

        var history = await _sut.GetHistoryAsync(entry.Id);
        var revision = Assert.Single(history);
        Assert.Equal("Primeira versão.", revision.PreviousText);
    }

    [Fact]
    public async Task UpdateTranslationAsync_FirstTranslationEver_DoesNotCreateARevision()
    {
        await _sut.ReplaceStringsAsync(_projectId, Stbl, [(1u, "Hello.")]);
        var entry = (await _sut.GetStringsAsync(_projectId))[0];

        await _sut.UpdateTranslationAsync(entry.Id, "Olá.", ProjectStringStatus.Translated, null);

        var history = await _sut.GetHistoryAsync(entry.Id);
        Assert.Empty(history);
    }

    [Fact]
    public async Task GetHistoryAsync_MultipleEdits_ReturnsMostRecentFirst()
    {
        await _sut.ReplaceStringsAsync(_projectId, Stbl, [(1u, "Hello.")]);
        var entry = (await _sut.GetStringsAsync(_projectId))[0];
        await _sut.UpdateTranslationAsync(entry.Id, "V1", ProjectStringStatus.Translated, null);
        await _sut.UpdateTranslationAsync(entry.Id, "V2", ProjectStringStatus.ManuallyEdited, null);
        await _sut.UpdateTranslationAsync(entry.Id, "V3", ProjectStringStatus.ManuallyEdited, null);

        var history = await _sut.GetHistoryAsync(entry.Id);

        Assert.Equal(2, history.Count);
        Assert.Equal("V2", history[0].PreviousText);
        Assert.Equal("V1", history[1].PreviousText);
    }
}
