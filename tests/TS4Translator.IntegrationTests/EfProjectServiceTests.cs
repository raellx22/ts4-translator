using System.Globalization;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Packages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TS4Translator.Core.Projects;
using TS4Translator.Core.Providers;
using TS4Translator.Core.Security;
using TS4Translator.Persistence;

namespace TS4Translator.IntegrationTests;

public sealed class EfProjectServiceTests : IDisposable
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("ts4translator-project-tests-").FullName;
    private readonly ServiceProvider _services;
    private readonly IProjectService _sut;

    public EfProjectServiceTests()
    {
        var databasePath = Path.Combine(_tempDirectory, "projects.db");
        _services = new ServiceCollection()
            .AddPersistenceServices(databasePath)
            .AddSingleton<ISecretStore>(new FakeSecretStore())
            .BuildServiceProvider();

        using var context = _services.GetRequiredService<IDbContextFactory<TranslatorDbContext>>().CreateDbContext();
        context.Database.Migrate();

        _sut = _services.GetRequiredService<IProjectService>();
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
    public async Task CreateAsync_ThenGetAsync_RoundTripsAllFields()
    {
        var created = await _sut.CreateAsync("WickedWhims", ["C:\\Mods\\a.package", "C:\\Mods\\b.package"], English, PortugueseBrazil);

        var fetched = await _sut.GetAsync(created.Id);

        Assert.NotNull(fetched);
        Assert.Equal("WickedWhims", fetched.Name);
        Assert.Equal(2, fetched.SourcePackagePaths.Count);
        Assert.Equal(English, fetched.SourceLocale);
        Assert.Equal(PortugueseBrazil, fetched.TargetLocale);
    }

    [Fact]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        var result = await _sut.GetAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task ListRecentAsync_ReturnsMostRecentlyUpdatedFirst()
    {
        var first = await _sut.CreateAsync("Older", [], English, PortugueseBrazil);
        await Task.Delay(10);
        var second = await _sut.CreateAsync("Newer", [], English, PortugueseBrazil);

        var recent = await _sut.ListRecentAsync();

        Assert.Equal(second.Id, recent[0].Id);
        Assert.Equal(first.Id, recent[1].Id);
    }

    [Fact]
    public async Task ListRecentAsync_RespectsMaxCount()
    {
        for (var i = 0; i < 5; i++)
        {
            await _sut.CreateAsync($"Project {i}", [], English, PortugueseBrazil);
        }

        var recent = await _sut.ListRecentAsync(maxCount: 3);

        Assert.Equal(3, recent.Count);
    }

    [Fact]
    public async Task DeleteAsync_RemovesProjectAndItsSourceFiles()
    {
        var created = await _sut.CreateAsync("ToDelete", ["C:\\Mods\\a.package"], English, PortugueseBrazil);

        await _sut.DeleteAsync(created.Id);
        var fetched = await _sut.GetAsync(created.Id);

        Assert.Null(fetched);
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_DoesNotThrow()
    {
        await _sut.DeleteAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task CreateAsync_NoSourceFiles_IsAllowed()
    {
        var created = await _sut.CreateAsync("Empty project", [], English, PortugueseBrazil);

        Assert.Empty(created.SourcePackagePaths);
    }

    [Fact]
    public async Task CreateAsync_CustomInstructionsDefaultsToNull()
    {
        var created = await _sut.CreateAsync("New project", [], English, PortugueseBrazil);

        Assert.Null(created.CustomInstructions);
    }

    [Fact]
    public async Task UpdateCustomInstructionsAsync_ThenGetAsync_RoundTrips()
    {
        var created = await _sut.CreateAsync("Styled project", [], English, PortugueseBrazil);

        await _sut.UpdateCustomInstructionsAsync(created.Id, "Keep a playful, informal tone.");
        var fetched = await _sut.GetAsync(created.Id);

        Assert.Equal("Keep a playful, informal tone.", fetched!.CustomInstructions);
    }

    [Fact]
    public async Task UpdateCustomInstructionsAsync_EmptyString_ClearsToNull()
    {
        var created = await _sut.CreateAsync("Styled project", [], English, PortugueseBrazil);
        await _sut.UpdateCustomInstructionsAsync(created.Id, "Some style.");

        await _sut.UpdateCustomInstructionsAsync(created.Id, "   ");
        var fetched = await _sut.GetAsync(created.Id);

        Assert.Null(fetched!.CustomInstructions);
    }

    [Fact]
    public async Task UpdateCustomInstructionsAsync_UnknownId_DoesNotThrow()
    {
        await _sut.UpdateCustomInstructionsAsync(Guid.NewGuid(), "Anything");
    }
    [Fact]
    public async Task FindBySourceFilesAsync_SameFilesInADifferentOrder_ReturnsTheExistingProject()
    {
        var modA = Path.Combine(_tempDirectory, "ModA.package");
        var modB = Path.Combine(_tempDirectory, "ModB.package");
        var created = await _sut.CreateAsync("Mod", [modA, modB], English, PortugueseBrazil);

        var found = await _sut.FindBySourceFilesAsync([modB, modA]);

        Assert.Equal(created.Id, found!.Id);
    }

    [Fact]
    public async Task FindBySourceFilesAsync_DifferentFileSet_ReturnsNull()
    {
        var modA = Path.Combine(_tempDirectory, "ModA.package");
        await _sut.CreateAsync("Mod", [modA], English, PortugueseBrazil);

        Assert.Null(await _sut.FindBySourceFilesAsync([Path.Combine(_tempDirectory, "Other.package")]));
    }

    [Fact]
    public async Task FindBySourceFilesAsync_SupersetOfAnExistingProjectsFiles_ReturnsNull()
    {
        var modA = Path.Combine(_tempDirectory, "ModA.package");
        var modB = Path.Combine(_tempDirectory, "ModB.package");
        await _sut.CreateAsync("Mod", [modA], English, PortugueseBrazil);

        Assert.Null(await _sut.FindBySourceFilesAsync([modA, modB]));
    }

    [Fact]
    public async Task FindBySourceFilesAsync_NoFiles_ReturnsNull()
    {
        await _sut.CreateAsync("Empty", [], English, PortugueseBrazil);

        Assert.Null(await _sut.FindBySourceFilesAsync([]));
    }
    /// <summary>
    /// Deleting a project is irreversible and the Home button now offers it, so what it takes with it
    /// has to be proven rather than assumed: strings, their revision history, per-project engine
    /// settings and project-scoped glossary terms all go by database cascade.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_TakesStringsRevisionsPipelineAndGlossaryWithIt()
    {
        var project = await _sut.CreateAsync("Para apagar", [], English, PortugueseBrazil);
        var strings = _services.GetRequiredService<IProjectStringService>();
        var profiles = _services.GetRequiredService<IProviderProfileService>();
        var glossary = _services.GetRequiredService<IGlossaryService>();

        var table = new ImportedTable("C:/Mods/X.package", new StblKey(0, 0x1122UL));
        await strings.ReplaceStringsAsync(project.Id, table, [(1u, "Hello."), (2u, "Bye.")]);
        var entry = (await strings.GetStringsAsync(project.Id))[0];
        await strings.UpdateTranslationAsync(entry.Id, "V1", ProjectStringStatus.Translated, null);
        await strings.UpdateTranslationAsync(entry.Id, "V2", ProjectStringStatus.ManuallyEdited, null);

        var argos = await profiles.SaveAsync(null, "Argos", ProviderKind.Argos, "local://argos", "m", 0.0, null);
        await profiles.SavePipelineSettingsAsync(new TranslationPipelineSettings(argos.Id), project.Id);
        await glossary.AddTermAsync("Sim", "Sim", neverTranslate: false, caseSensitive: false, notes: null, project.Id);

        Assert.NotEmpty(await strings.GetHistoryAsync(entry.Id));

        await _sut.DeleteAsync(project.Id);

        Assert.Null(await _sut.GetAsync(project.Id));
        Assert.Empty(await strings.GetStringsAsync(project.Id));
        Assert.Empty(await strings.GetHistoryAsync(entry.Id));
        Assert.False(await profiles.HasPipelineOverrideAsync(project.Id));
        Assert.Empty(await glossary.GetTermsAsync(project.Id));

        // The engine profile itself is app-wide and must survive - only the project goes.
        Assert.NotNull(await profiles.GetAsync(argos.Id));
    }

    [Fact]
    public async Task DeleteAsync_OneProject_LeavesTheOthersAlone()
    {
        var keep = await _sut.CreateAsync("Fica", [], English, PortugueseBrazil);
        var drop = await _sut.CreateAsync("Sai", [], English, PortugueseBrazil);
        var strings = _services.GetRequiredService<IProjectStringService>();
        var table = new ImportedTable("C:/Mods/X.package", new StblKey(0, 0x1122UL));
        await strings.ReplaceStringsAsync(keep.Id, table, [(1u, "Fica.")]);
        await strings.ReplaceStringsAsync(drop.Id, table, [(1u, "Sai.")]);

        await _sut.DeleteAsync(drop.Id);

        Assert.NotNull(await _sut.GetAsync(keep.Id));
        Assert.Single(await strings.GetStringsAsync(keep.Id));
    }
}
