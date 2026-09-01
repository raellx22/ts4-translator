using TS4Translator.Core.Glossary;

namespace TS4Translator.Core.Tests;

public sealed class DefaultGlossaryTests
{
    private static GlossaryTerm AsTerm(DefaultGlossaryTerm seed) =>
        new(0, seed.Original, seed.Original, NeverTranslate: true, CaseSensitive: true, seed.Notes, ProjectId: null);

    [Fact]
    public void Terms_AreAllExplained()
        => Assert.All(DefaultGlossary.Terms, term =>
        {
            Assert.False(string.IsNullOrWhiteSpace(term.Original));
            Assert.False(string.IsNullOrWhiteSpace(term.Notes));
        });

    [Fact]
    public void Terms_HaveNoDuplicates()
        => Assert.Equal(
            DefaultGlossary.Terms.Count,
            DefaultGlossary.Terms.Select(term => term.Original).Distinct(StringComparer.OrdinalIgnoreCase).Count());

    /// <summary>
    /// Terms match as substrings, so a short one fires inside unrelated words. "Sim" would hit
    /// "Similar", "Simple" and "Simulation" — and is an ordinary Portuguese word besides.
    /// </summary>
    [Theory]
    [InlineData("Sim")]
    [InlineData("EA")]
    [InlineData("CC")]
    public void Terms_ExcludeShortAmbiguousNames(string dangerous)
        => Assert.DoesNotContain(DefaultGlossary.Terms, term =>
            string.Equals(term.Original, dangerous, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The seed is names only. Retention mining also ranked ordinary words the engine merely fails to
    /// translate ("Lap", "Leggings", "Shampoo"); protecting those would freeze a bug as a rule.
    /// </summary>
    [Theory]
    [InlineData("Lap")]
    [InlineData("Leggings")]
    [InlineData("Shampoo")]
    [InlineData("Buffs")]
    [InlineData("Broadcaster")]
    public void Terms_ExcludeOrdinaryWordsThatMerelyFailToTranslate(string word)
        => Assert.DoesNotContain(DefaultGlossary.Terms, term =>
            string.Equals(term.Original, word, StringComparison.OrdinalIgnoreCase));

    [Theory]
    [InlineData("WickedWhims")]
    [InlineData("Sims")]
    [InlineData("Simoleon")]
    public void Terms_IncludeTheNamesMeasuredAsCorrupted(string expected)
        => Assert.Contains(DefaultGlossary.Terms, term =>
            string.Equals(term.Original, expected, StringComparison.Ordinal));

    /// <summary>A seeded term must actually fire on the text it was added for.</summary>
    [Fact]
    public void Terms_MatchTheTextTheyProtect()
    {
        var wickedWhims = AsTerm(DefaultGlossary.Terms.Single(term => term.Original == "WickedWhims"));

        Assert.True(wickedWhims.AppearsIn("Visit WickedWhims Extra Downloads"));
        Assert.False(wickedWhims.AppearsIn("Visit the downloads page"));
    }

    /// <summary>
    /// Case-sensitive on purpose: these are names, and matching is by substring, so "CAS" must not fire
    /// on ordinary prose that merely contains those letters.
    /// </summary>
    [Fact]
    public void Terms_AreCaseSensitiveSoTheyDoNotFireOnProse()
    {
        var cas = AsTerm(DefaultGlossary.Terms.Single(term => term.Original == "CAS"));

        Assert.True(cas.AppearsIn("Open CAS to edit the Sim"));
        Assert.False(cas.AppearsIn("in some cases this is ignored"));
    }
}
