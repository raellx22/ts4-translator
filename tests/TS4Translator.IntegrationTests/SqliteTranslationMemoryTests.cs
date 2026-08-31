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

    [Fact]
    public async Task FindSimilarAsync_NearDuplicateAboveThreshold_IsFound()
    {
        await _sut.SaveAsync(new TranslationMemoryEntry("Ask about their old job.", English, PortugueseBrazil, "Perguntar sobre o antigo emprego.", "argos", null, DateTimeOffset.UtcNow));

        var found = await _sut.FindSimilarAsync("Ask about their new job.", English, PortugueseBrazil, minSimilarity: 0.85);

        Assert.NotNull(found);
        Assert.Equal("Perguntar sobre o antigo emprego.", found.Entry.TranslatedText);
        Assert.True(found.Similarity is > 0.85 and < 1.0);
    }

    [Fact]
    public async Task FindSimilarAsync_CompletelyDifferentText_ReturnsNull()
    {
        await _sut.SaveAsync(new TranslationMemoryEntry("Ask about their old job.", English, PortugueseBrazil, "Perguntar sobre o antigo emprego.", "argos", null, DateTimeOffset.UtcNow));

        var found = await _sut.FindSimilarAsync("The weather is nice today.", English, PortugueseBrazil, minSimilarity: 0.85);

        Assert.Null(found);
    }

    [Fact]
    public async Task FindSimilarAsync_BelowRequestedThreshold_ReturnsNull()
    {
        await _sut.SaveAsync(new TranslationMemoryEntry("Ask about their old job.", English, PortugueseBrazil, "Perguntar sobre o antigo emprego.", "argos", null, DateTimeOffset.UtcNow));

        // Same near-duplicate as the "above threshold" test, but demanding near-exact similarity.
        var found = await _sut.FindSimilarAsync("Ask about their new job.", English, PortugueseBrazil, minSimilarity: 0.999);

        Assert.Null(found);
    }

    [Fact]
    public async Task FindSimilarAsync_PicksTheClosestOfMultipleCandidates()
    {
        await _sut.SaveAsync(new TranslationMemoryEntry("Ask about their old job.", English, PortugueseBrazil, "Versão distante.", "argos", null, DateTimeOffset.UtcNow));
        await _sut.SaveAsync(new TranslationMemoryEntry("Ask about their new job!", English, PortugueseBrazil, "Versão próxima.", "argos", null, DateTimeOffset.UtcNow));

        var found = await _sut.FindSimilarAsync("Ask about their new job.", English, PortugueseBrazil, minSimilarity: 0.5);

        Assert.NotNull(found);
        Assert.Equal("Versão próxima.", found.Entry.TranslatedText);
    }

    [Fact]
    public async Task FindSimilarAsync_DifferentTargetLocale_DoesNotCrossContaminate()
    {
        await _sut.SaveAsync(new TranslationMemoryEntry("Ask about their old job.", English, French, "Version française.", "argos", null, DateTimeOffset.UtcNow));

        var found = await _sut.FindSimilarAsync("Ask about their new job.", English, PortugueseBrazil, minSimilarity: 0.85);

        Assert.Null(found);
    }

    // ---------- browsing and housekeeping ----------

    private async Task SeedAsync(params (string Original, string Translation, string Provider)[] entries)
    {
        foreach (var (original, translation, provider) in entries)
        {
            await _sut.SaveAsync(new TranslationMemoryEntry(original, English, PortugueseBrazil, translation, provider, null, DateTimeOffset.UtcNow));
        }
    }

    [Fact]
    public async Task SearchAsync_MatchesEitherSideOfTheEntry()
    {
        await SeedAsync(("Shallow", "Raso", "argos"), ("Average Hips", "Quadril médio", "argos"));

        Assert.Equal("Raso", Assert.Single((await _sut.SearchAsync("shallow", null, 0, 50)).Entries).TranslatedText);
        Assert.Equal("Raso", Assert.Single((await _sut.SearchAsync("RASO", null, 0, 50)).Entries).TranslatedText);
    }

    [Fact]
    public async Task SearchAsync_FiltersByProvider()
    {
        await SeedAsync(("Shallow", "Raso", "argos"), ("Average Hips", "Quadril médio", "manual"));

        var page = await _sut.SearchAsync(null, "manual", 0, 50);

        Assert.Equal("Quadril médio", Assert.Single(page.Entries).TranslatedText);
        Assert.Equal(1, page.TotalMatches);
    }

    /// <summary>The page has to report what it is not showing, or "próximas" cannot know when to stop.</summary>
    [Fact]
    public async Task SearchAsync_Paging_ReportsTheFullMatchCount()
    {
        await SeedAsync([.. Enumerable.Range(1, 10).Select(index => ($"Texto {index}", $"Tradução {index}", "argos"))]);

        var page = await _sut.SearchAsync(null, null, skip: 4, take: 3);

        Assert.Equal(3, page.Entries.Count);
        Assert.Equal(10, page.TotalMatches);
    }

    [Fact]
    public async Task SearchAsync_PagesDoNotOverlapOrSkipEntries()
    {
        await SeedAsync([.. Enumerable.Range(1, 25).Select(index => ($"Texto {index}", $"Tradução {index}", "argos"))]);

        var first = await _sut.SearchAsync(null, null, 0, 10);
        var second = await _sut.SearchAsync(null, null, 10, 10);
        var third = await _sut.SearchAsync(null, null, 20, 10);

        var seen = first.Entries.Concat(second.Entries).Concat(third.Entries).Select(entry => entry.OriginalText).ToList();
        Assert.Equal(25, seen.Count);
        Assert.Equal(25, seen.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task CountByProviderAsync_GroupsByOrigin()
    {
        await SeedAsync(("A", "a", "argos"), ("B", "b", "argos"), ("C", "c", "manual"));

        var counts = await _sut.CountByProviderAsync();

        Assert.Equal(2, counts.Single(count => count.Provider == "argos").Count);
        Assert.Equal(1, counts.Single(count => count.Provider == "manual").Count);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyThatEntry()
    {
        await SeedAsync(("Shallow", "Raso", "argos"), ("Average Hips", "Quadril médio", "argos"));

        Assert.True(await _sut.DeleteAsync("Shallow", English, PortugueseBrazil));

        Assert.Null(await _sut.FindExactAsync("Shallow", English, PortugueseBrazil));
        Assert.NotNull(await _sut.FindExactAsync("Average Hips", English, PortugueseBrazil));
    }

    [Fact]
    public async Task DeleteAsync_UnknownEntry_ReportsNothingRemoved()
        => Assert.False(await _sut.DeleteAsync("Nunca existiu", English, PortugueseBrazil));

    /// <summary>The escape hatch for a memory filled by an engine the user has stopped trusting.</summary>
    [Fact]
    public async Task DeleteByProviderAsync_EmptiesOneOriginAndLeavesTheRest()
    {
        await SeedAsync(("A", "a", "argos"), ("B", "b", "argos"), ("C", "c", "manual"));

        Assert.Equal(2, await _sut.DeleteByProviderAsync("argos"));

        Assert.Equal("manual", Assert.Single(await _sut.CountByProviderAsync()).Provider);
    }

    /// <summary>
    /// A human correction has to win over the machine entry it replaces — same key, so it overwrites
    /// rather than piling up a second answer for the same source text.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ManualCorrectionOverAMachineEntry_ReplacesIt()
    {
        await SeedAsync(("Shallow", "Rasgo", "argos"));

        await SeedAsync(("Shallow", "Raso", TranslationMemoryPolicy.ManualProvider));

        var found = await _sut.FindExactAsync("Shallow", English, PortugueseBrazil);
        Assert.Equal("Raso", found?.TranslatedText);
        Assert.Equal(TranslationMemoryPolicy.ManualProvider, found?.Provider);
        Assert.Equal(1, (await _sut.SearchAsync(null, null, 0, 50)).TotalMatches);
    }
}
