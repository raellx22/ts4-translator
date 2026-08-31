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
    private static readonly ImportedTable Stbl = new("C:/Mods/Test.package", new StblKey(Group: 0, FullInstance: 0x1122UL));
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
        Assert.All(strings, s => Assert.Equal(Stbl.Stbl, s.SourceStbl));
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
    public async Task UpdateTranslationsAsync_PersistsAChunkAndKeepsPreviousTranslationsAsRevisions()
    {
        await _sut.ReplaceStringsAsync(_projectId, Stbl, [(1u, "Hello."), (2u, "Bye.")]);
        var entries = (await _sut.GetStringsAsync(_projectId)).OrderBy(entry => entry.KeyHash).ToList();
        await _sut.UpdateTranslationAsync(entries[0].Id, "Versão anterior.", ProjectStringStatus.Translated, null);

        await _sut.UpdateTranslationsAsync(
        [
            new ProjectStringTranslationUpdate(entries[0].Id, "Olá.", ProjectStringStatus.ManuallyEdited, null),
            new ProjectStringTranslationUpdate(entries[1].Id, "Tchau.", ProjectStringStatus.NeedsReview, "Revisar termo."),
        ]);

        var updated = (await _sut.GetStringsAsync(_projectId)).ToDictionary(entry => entry.Id);
        Assert.Equal("Olá.", updated[entries[0].Id].TranslatedText);
        Assert.Equal(ProjectStringStatus.ManuallyEdited, updated[entries[0].Id].Status);
        Assert.Equal("Tchau.", updated[entries[1].Id].TranslatedText);
        Assert.Equal("Revisar termo.", updated[entries[1].Id].IssueSummary);
        Assert.Equal("Versão anterior.", Assert.Single(await _sut.GetHistoryAsync(entries[0].Id)).PreviousText);
        Assert.Empty(await _sut.GetHistoryAsync(entries[1].Id));
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
    [Fact]
    public async Task RemoveStringsOutsideAsync_DropsTablesTheImportNoLongerCovers()
    {
        var otherLocale = new ImportedTable("C:/Mods/Test.package", new StblKey(Group: 0, FullInstance: 0x1100_0000_0000_1122UL));
        await _sut.ReplaceStringsAsync(_projectId, Stbl, [(1u, "Hello."), (2u, "Bye.")]);
        await _sut.ReplaceStringsAsync(_projectId, otherLocale, [(1u, "Ola.")]);

        var removed = await _sut.RemoveStringsOutsideAsync(_projectId, [Stbl]);

        Assert.Equal(1, removed);
        var remaining = await _sut.GetStringsAsync(_projectId);
        Assert.Equal(2, remaining.Count);
        Assert.All(remaining, entry => Assert.Equal(Stbl.Stbl, entry.SourceStbl));
    }

    [Fact]
    public async Task RemoveStringsOutsideAsync_EverythingStillImported_RemovesNothing()
    {
        await _sut.ReplaceStringsAsync(_projectId, Stbl, [(1u, "Hello.")]);

        Assert.Equal(0, await _sut.RemoveStringsOutsideAsync(_projectId, [Stbl]));
        Assert.Single(await _sut.GetStringsAsync(_projectId));
    }

    [Fact]
    public async Task RemoveStringsOutsideAsync_AlsoDropsTheirRevisionHistory()
    {
        await _sut.ReplaceStringsAsync(_projectId, Stbl, [(1u, "Hello.")]);
        var entry = (await _sut.GetStringsAsync(_projectId))[0];
        await _sut.UpdateTranslationAsync(entry.Id, "V1", ProjectStringStatus.Translated, null);
        await _sut.UpdateTranslationAsync(entry.Id, "V2", ProjectStringStatus.ManuallyEdited, null);
        Assert.NotEmpty(await _sut.GetHistoryAsync(entry.Id));

        await _sut.RemoveStringsOutsideAsync(_projectId, [new ImportedTable("C:/Mods/Other.package", new StblKey(Group: 99, FullInstance: 0UL))]);

        Assert.Empty(await _sut.GetStringsAsync(_projectId));
        Assert.Empty(await _sut.GetHistoryAsync(entry.Id));
    }
    /// <summary>
    /// Two unrelated mods imported into the same project may ship an STBL with the very same group
    /// and instance id. Without the source file in the key, importing the second would wipe the first.
    /// </summary>
    [Fact]
    public async Task ReplaceStringsAsync_TwoModsSharingAnStblKey_KeepsBothSetsOfStrings()
    {
        var fromModA = new ImportedTable("C:/Mods/ModA.package", Stbl.Stbl);
        var fromModB = new ImportedTable("C:/Mods/ModB.package", Stbl.Stbl);

        await _sut.ReplaceStringsAsync(_projectId, fromModA, [(1u, "From mod A")]);
        await _sut.ReplaceStringsAsync(_projectId, fromModB, [(1u, "From mod B")]);

        var strings = await _sut.GetStringsAsync(_projectId);

        Assert.Equal(2, strings.Count);
        Assert.Contains(strings, s => s.OriginalText == "From mod A" && s.SourceFile!.EndsWith("ModA.package", StringComparison.Ordinal));
        Assert.Contains(strings, s => s.OriginalText == "From mod B" && s.SourceFile!.EndsWith("ModB.package", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RemoveStringsOutsideAsync_ModNoLongerImported_DropsOnlyThatModsStrings()
    {
        var fromModA = new ImportedTable("C:/Mods/ModA.package", Stbl.Stbl);
        var fromModB = new ImportedTable("C:/Mods/ModB.package", Stbl.Stbl);
        await _sut.ReplaceStringsAsync(_projectId, fromModA, [(1u, "From mod A")]);
        await _sut.ReplaceStringsAsync(_projectId, fromModB, [(1u, "From mod B")]);

        var removed = await _sut.RemoveStringsOutsideAsync(_projectId, [fromModA]);

        Assert.Equal(1, removed);
        Assert.Equal("From mod A", Assert.Single(await _sut.GetStringsAsync(_projectId)).OriginalText);
    }
    /// <summary>
    /// The regression that destroyed a real 1,351-string project: rows imported before the app tracked
    /// source files carry SourceFile NULL, and the first re-import that names their file has to adopt
    /// them - keeping their translations - instead of inserting duplicates and letting the prune delete
    /// the originals.
    /// </summary>
    [Fact]
    public async Task ReplaceStringsAsync_LegacyRowsWithoutASourceFile_AreAdoptedAndKeepTheirTranslations()
    {
        var legacy = new ImportedTable(null, Stbl.Stbl);
        var named = new ImportedTable("C:/Mods/Test.package", Stbl.Stbl);

        await _sut.ReplaceStringsAsync(_projectId, legacy, [(1u, "Hello."), (2u, "Bye.")]);
        var before = await _sut.GetStringsAsync(_projectId);
        await _sut.UpdateTranslationAsync(before[0].Id, "Ola.", ProjectStringStatus.Translated, null);
        await _sut.UpdateTranslationAsync(before[1].Id, "Tchau.", ProjectStringStatus.ManuallyEdited, null);

        var diff = await _sut.ReplaceStringsAsync(_projectId, named, [(1u, "Hello."), (2u, "Bye.")]);
        var pruned = await _sut.RemoveStringsOutsideAsync(_projectId, [named]);

        Assert.Equal(0, diff.NewCount);
        Assert.Equal(2, diff.UnchangedCount);
        Assert.Equal(0, pruned);

        var after = await _sut.GetStringsAsync(_projectId);
        Assert.Equal(2, after.Count);
        Assert.All(after, entry => Assert.EndsWith("Test.package", entry.SourceFile!, StringComparison.Ordinal));
        Assert.Contains(after, entry => entry.TranslatedText == "Ola." && entry.Status == ProjectStringStatus.Translated);
        Assert.Contains(after, entry => entry.TranslatedText == "Tchau." && entry.Status == ProjectStringStatus.ManuallyEdited);
    }

    [Fact]
    public async Task ReplaceStringsAsync_LegacyAndNamedRowsForTheSameHash_KeepsOnlyTheNamedOne()
    {
        var legacy = new ImportedTable(null, Stbl.Stbl);
        var named = new ImportedTable("C:/Mods/Test.package", Stbl.Stbl);

        await _sut.ReplaceStringsAsync(_projectId, legacy, [(1u, "Antigo")]);
        await _sut.ReplaceStringsAsync(_projectId, named, [(1u, "Novo")]);

        // Two rows for one hash would break the unique index the moment the legacy one is adopted.
        var only = Assert.Single(await _sut.GetStringsAsync(_projectId));
        Assert.Equal("Novo", only.OriginalText);
    }

    [Fact]
    public async Task ReplaceStringsAsync_LegacyRowWhoseTextChanged_IsAdoptedAndFlaggedForReview()
    {
        var legacy = new ImportedTable(null, Stbl.Stbl);
        var named = new ImportedTable("C:/Mods/Test.package", Stbl.Stbl);

        await _sut.ReplaceStringsAsync(_projectId, legacy, [(1u, "Texto antigo")]);
        var entry = (await _sut.GetStringsAsync(_projectId))[0];
        await _sut.UpdateTranslationAsync(entry.Id, "Traducao", ProjectStringStatus.Translated, null);

        await _sut.ReplaceStringsAsync(_projectId, named, [(1u, "Texto novo")]);

        var after = Assert.Single(await _sut.GetStringsAsync(_projectId));
        Assert.Equal("Traducao", after.TranslatedText);
        Assert.Equal(ProjectStringStatus.NeedsReview, after.Status);
        Assert.NotNull(after.SourceFile);
    }
}
