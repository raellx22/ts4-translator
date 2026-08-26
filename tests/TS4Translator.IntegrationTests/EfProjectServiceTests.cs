using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TS4Translator.Core.Projects;
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
        _services = new ServiceCollection().AddPersistenceServices(databasePath).BuildServiceProvider();

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
}
