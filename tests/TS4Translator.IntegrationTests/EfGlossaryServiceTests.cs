using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Projects;
using TS4Translator.Persistence;

namespace TS4Translator.IntegrationTests;

public sealed class EfGlossaryServiceTests : IDisposable
{
    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("ts4translator-glossary-tests-").FullName;
    private readonly ServiceProvider _services;
    private readonly IGlossaryService _sut;
    private readonly IProjectService _projectService;
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    public EfGlossaryServiceTests()
    {
        var databasePath = Path.Combine(_tempDirectory, "glossary.db");
        _services = new ServiceCollection().AddPersistenceServices(databasePath).BuildServiceProvider();

        using var context = _services.GetRequiredService<IDbContextFactory<TranslatorDbContext>>().CreateDbContext();
        context.Database.Migrate();

        _sut = _services.GetRequiredService<IGlossaryService>();
        _projectService = _services.GetRequiredService<IProjectService>();
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
    public async Task AddTermAsync_ThenGetTermsAsync_RoundTrips()
    {
        await _sut.AddTermAsync("Grim Reaper", "Ceifador", neverTranslate: false, caseSensitive: false, notes: "Death NPC", projectId: null);

        var terms = await _sut.GetTermsAsync(projectId: null);

        var term = Assert.Single(terms);
        Assert.Equal("Grim Reaper", term.Original);
        Assert.Equal("Ceifador", term.Translation);
        Assert.Equal("Death NPC", term.Notes);
    }

    [Fact]
    public async Task GetTermsAsync_GlobalTerm_IsVisibleToEveryProject()
    {
        await _sut.AddTermAsync("SimCity", "SimCity", neverTranslate: true, caseSensitive: false, notes: null, projectId: null);
        var project = await _projectService.CreateAsync("Some Mod", [], English, PortugueseBrazil);

        var terms = await _sut.GetTermsAsync(project.Id);

        Assert.Single(terms);
    }

    [Fact]
    public async Task GetTermsAsync_ProjectSpecificTerm_IsNotVisibleToOtherProjects()
    {
        var projectA = await _projectService.CreateAsync("Mod A", [], English, PortugueseBrazil);
        var projectB = await _projectService.CreateAsync("Mod B", [], English, PortugueseBrazil);
        await _sut.AddTermAsync("Aspiration", "Aspiração", neverTranslate: false, caseSensitive: false, notes: null, projectId: projectA.Id);

        var termsForA = await _sut.GetTermsAsync(projectA.Id);
        var termsForB = await _sut.GetTermsAsync(projectB.Id);

        Assert.Single(termsForA);
        Assert.Empty(termsForB);
    }

    [Fact]
    public async Task GetTermsAsync_MixOfGlobalAndProjectTerms_ReturnsBoth()
    {
        var project = await _projectService.CreateAsync("Some Mod", [], English, PortugueseBrazil);
        await _sut.AddTermAsync("SimCity", "SimCity", neverTranslate: true, caseSensitive: false, notes: null, projectId: null);
        await _sut.AddTermAsync("Aspiration", "Aspiração", neverTranslate: false, caseSensitive: false, notes: null, projectId: project.Id);

        var terms = await _sut.GetTermsAsync(project.Id);

        Assert.Equal(2, terms.Count);
    }

    [Fact]
    public async Task DeleteTermAsync_RemovesTheTerm()
    {
        var term = await _sut.AddTermAsync("Grim Reaper", "Ceifador", neverTranslate: false, caseSensitive: false, notes: null, projectId: null);

        await _sut.DeleteTermAsync(term.Id);
        var terms = await _sut.GetTermsAsync(projectId: null);

        Assert.Empty(terms);
    }

    [Fact]
    public async Task DeleteTermAsync_UnknownId_DoesNotThrow()
    {
        await _sut.DeleteTermAsync(999);
    }
}
