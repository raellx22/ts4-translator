using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TS4Translator.Core.Memory;
using TS4Translator.Persistence;

namespace TS4Translator.IntegrationTests;

/// <summary>Exercises the real SQLite-backed translation memory end to end, migrations included.</summary>
public sealed class SqliteTranslationMemoryTests : IDisposable
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("ts4translator-tm-tests-").FullName;
    private readonly ServiceProvider _services;
    private readonly ITranslationMemory _sut;

    public SqliteTranslationMemoryTests()
    {
        var databasePath = Path.Combine(_tempDirectory, "memory.db");
        _services = new ServiceCollection()
            .AddPersistenceServices(databasePath)
            .BuildServiceProvider();

        using var scopeContext = _services.GetRequiredService<IDbContextFactory<TranslatorDbContext>>().CreateDbContext();
        scopeContext.Database.Migrate();

        _sut = _services.GetRequiredService<ITranslationMemory>();
    }

    public void Dispose()
    {
        _services.Dispose();
        // Microsoft.Data.Sqlite pools connections at the native level independently of DbContext
        // disposal; without clearing the pool the file stays locked and cleanup below throws.
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task FindExactAsync_NoEntrySaved_ReturnsNull()
    {
        var result = await _sut.FindExactAsync("Hello there.", English, PortugueseBrazil);

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_ThenFindExactAsync_RoundTripsTheEntry()
    {
        var entry = new TranslationMemoryEntry("WooHoo!", English, PortugueseBrazil, "Oba-oba!", "argos", "the-sims", DateTimeOffset.UtcNow);

        await _sut.SaveAsync(entry);
        var found = await _sut.FindExactAsync("WooHoo!", English, PortugueseBrazil);

        Assert.NotNull(found);
        Assert.Equal("Oba-oba!", found.TranslatedText);
        Assert.Equal("argos", found.Provider);
        Assert.Equal("the-sims", found.ProfileName);
    }

    [Fact]
    public async Task SaveAsync_CalledTwiceForSameSourceAndLocalePair_UpdatesRatherThanDuplicates()
    {
        var first = new TranslationMemoryEntry("Ask about their day.", English, PortugueseBrazil, "Primeira versão.", "argos", null, DateTimeOffset.UtcNow);
        var second = first with { TranslatedText = "Versão revisada.", Provider = "manual" };

        await _sut.SaveAsync(first);
        await _sut.SaveAsync(second);
        var found = await _sut.FindExactAsync("Ask about their day.", English, PortugueseBrazil);

        Assert.Equal("Versão revisada.", found!.TranslatedText);
        Assert.Equal("manual", found.Provider);
    }

    [Fact]
    public async Task FindExactAsync_SameTextDifferentTargetLocale_DoesNotCrossContaminate()
    {
        await _sut.SaveAsync(new TranslationMemoryEntry("Good morning.", English, PortugueseBrazil, "Bom dia.", "argos", null, DateTimeOffset.UtcNow));
        await _sut.SaveAsync(new TranslationMemoryEntry("Good morning.", English, French, "Bonjour.", "argos", null, DateTimeOffset.UtcNow));

        var portuguese = await _sut.FindExactAsync("Good morning.", English, PortugueseBrazil);
        var french = await _sut.FindExactAsync("Good morning.", English, French);

        Assert.Equal("Bom dia.", portuguese!.TranslatedText);
        Assert.Equal("Bonjour.", french!.TranslatedText);
    }

    [Fact]
    public async Task FindExactAsync_TextDiffersOnlyBySurroundingWhitespace_StillMatches()
    {
        await _sut.SaveAsync(new TranslationMemoryEntry("Ask about the party.", English, PortugueseBrazil, "Perguntar sobre a festa.", "argos", null, DateTimeOffset.UtcNow));

        var found = await _sut.FindExactAsync("  Ask about the party.  ", English, PortugueseBrazil);

        Assert.NotNull(found);
    }
}
