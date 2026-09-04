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
    /// What "apagar o projeto e refazer do zero" needs: the lines of one mod forgotten in one pass,
    /// without touching anything else the memory holds.
    /// </summary>
    [Fact]
    public async Task DeleteManyAsync_ForgetsOnlyTheGivenTextsForThatLocalePair()
    {
        await SeedAsync(("Shallow", "Raso", "argos"), ("Average Hips", "Quadril médio", "argos"), ("Deep", "Fundo", "argos"));
        await _sut.SaveAsync(new TranslationMemoryEntry("Shallow", English, French, "Peu profond", "argos", null, DateTimeOffset.UtcNow));

        var removed = await _sut.DeleteManyAsync(["Shallow", "Deep", "Nunca existiu"], English, PortugueseBrazil);

        Assert.Equal(2, removed);
        Assert.Null(await _sut.FindExactAsync("Shallow", English, PortugueseBrazil));
        Assert.Null(await _sut.FindExactAsync("Deep", English, PortugueseBrazil));
        Assert.NotNull(await _sut.FindExactAsync("Average Hips", English, PortugueseBrazil));
        // Another locale pair is another entry entirely, and is none of this call's business.
        Assert.NotNull(await _sut.FindExactAsync("Shallow", English, French));
    }

    /// <summary>Chunked internally to stay under SQLite's parameter cap — a real mod is thousands of lines.</summary>
    [Fact]
    public async Task DeleteManyAsync_MoreTextsThanOneStatementHolds_RemovesThemAll()
    {
        var texts = Enumerable.Range(0, 1200).Select(index => $"Line {index}").ToList();
        await _sut.SaveManyAsync(
            [.. texts.Select(text => new TranslationMemoryEntry(text, English, PortugueseBrazil, $"Linha {text}", "argos", null, DateTimeOffset.UtcNow))]);

        Assert.Equal(1200, await _sut.DeleteManyAsync(texts, English, PortugueseBrazil));
        Assert.Equal(0, (await _sut.SearchAsync(null, null, 0, 50)).TotalMatches);
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

    /// <summary>
    /// The batch lookup used to load every row of the locale pair into memory and re-index it, once per
    /// chunk of a hundred strings and once per lane. It now reads exact hits off the unique index and
    /// asks for fuzzy candidates only in the length band that can possibly reach — so what it must prove
    /// is that neither shortcut lost an answer it used to find.
    /// </summary>
    [Fact]
    public async Task FindBestMatchesAsync_StillFindsExactAndNearMatchesAndNothingElse()
    {
        await SeedAsync(
            ("Shallow", "Raso", "argos"),
            ("Ask your sim to talk about their job.", "Peça ao seu sim para falar sobre o trabalho dele.", "manual"),
            ("Completely unrelated text of a similar sort of length.", "Texto sem relação.", "argos"));

        var results = await _sut.FindBestMatchesAsync(
            [
                "Shallow",                                    // exact
                "Ask your sim to talk about their job!",      // one edit in 37 characters: ~97.3%
                "Average Hips",                               // nothing stored
                "Ask your sim about their old job at the lot.",// ~93%, below the bar
            ],
            English, PortugueseBrazil, minSimilarity: 0.97);

        Assert.Equal(4, results.Count);
        Assert.Equal(1.0, results[0]!.Similarity);
        Assert.Equal("Raso", results[0]!.Entry.TranslatedText);

        Assert.NotNull(results[1]);
        Assert.InRange(results[1]!.Similarity, 0.97, 0.999);
        Assert.Equal("Peça ao seu sim para falar sobre o trabalho dele.", results[1]!.Entry.TranslatedText);

        Assert.Null(results[2]);
        Assert.Null(results[3]);
    }

    /// <summary>
    /// A short label cannot have a 97%-similar neighbour that is not identical, so the whole fuzzy pass
    /// is skipped for it. The answer must be the same as before: the exact hit, and nothing invented.
    /// </summary>
    [Fact]
    public async Task FindBestMatchesAsync_ShortLabels_AreAnsweredByHashAlone()
    {
        await SeedAsync(("Shallow", "Raso", "argos"), ("Shallows", "Rasos", "argos"));

        var results = await _sut.FindBestMatchesAsync(["Shallow", "Shallo"], English, PortugueseBrazil, minSimilarity: 0.97);

        Assert.Equal("Raso", results[0]!.Entry.TranslatedText);
        Assert.Null(results[1]);
    }

    [Fact]
    public async Task FindBestMatchesAsync_OtherLocalePair_IsNeverReturned()
    {
        await SeedAsync(("Shallow", "Raso", "argos"));

        var results = await _sut.FindBestMatchesAsync(["Shallow"], English, French, minSimilarity: 0.97);

        Assert.Null(Assert.Single(results));
    }
}
