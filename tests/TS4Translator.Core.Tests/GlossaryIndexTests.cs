using TS4Translator.Core.Glossary;

namespace TS4Translator.Core.Tests;

public class GlossaryIndexTests
{
    private static GlossaryTerm Term(int id, string original, string translation, bool neverTranslate = false, bool caseSensitive = false) =>
        new(id, original, translation, neverTranslate, caseSensitive, Notes: null, ProjectId: null);

    /// <summary>
    /// The index exists to be faster, so the one thing it must never be is different. Checked against
    /// the whole shipped list rather than a handful of examples, because the failure mode of a
    /// first-word bucket is a term whose shape nobody thought of.
    /// </summary>
    [Fact]
    public void MatchesIn_AgreesWithTheLinearSweep_AcrossTheShippedGlossary()
    {
        var terms = DefaultTermGlossary.Terms
            .Select((term, id) => Term(id, term.Original, term.Translation))
            .ToList();
        var index = GlossaryIndex.For(terms);

        string[] samples =
        [
            "Talk to the Grim Reaper about the Sims",
            "---------- Buffs ----------",
            "1 Test Left",
            "Body Selector",
            "debug_savedActor2",
            "Nothing here matches any rule whatsoever",
            "Male",
            "Female",
            "Hardcore",
            "male_torso_ref",
            "Sex Autonomy Settings and more Sex",
        ];

        foreach (var sample in samples)
        {
            var expected = terms.Where(term => term.AppearsIn(sample)).OrderBy(term => term.Id).ToList();
            var actual = index.MatchesIn(sample).OrderBy(term => term.Id).ToList();

            Assert.Equal(expected.Select(term => term.Original), actual.Select(term => term.Original));
            Assert.Equal(expected.Count > 0, index.HasMatchIn(sample));
        }
    }

    [Fact]
    public void MatchesIn_TermAppearingTwice_IsReturnedOnce()
    {
        var index = GlossaryIndex.For([Term(1, "Sex", "Sexo")]);

        var matches = index.MatchesIn("Sex, and then more Sex");

        Assert.Single(matches);
    }

    [Fact]
    public void MatchesIn_LongestFirst()
    {
        var index = GlossaryIndex.For([Term(1, "Sex", "Sexo"), Term(2, "Sex Autonomy Settings", "Configurações de Autonomia Sexual")]);

        var matches = index.MatchesIn("Open the Sex Autonomy Settings panel");

        Assert.Equal("Sex Autonomy Settings", matches[0].Original);
    }

    /// <summary>The boundary rule the index inherits: a rule must not fire inside a longer word.</summary>
    [Theory]
    [InlineData("Female", false)]
    [InlineData("Malevolent", false)]
    [InlineData("male_torso_ref", false)]
    [InlineData("(Male)", true)]
    [InlineData("Male:", true)]
    public void MatchesIn_RespectsWordBoundaries(string text, bool expected)
    {
        var index = GlossaryIndex.For([Term(1, "Male", "Masculino")]);

        Assert.Equal(expected, index.HasMatchIn(text));
    }

    [Fact]
    public void WholeStringMatch_IgnoresSurroundingWhitespace()
    {
        var index = GlossaryIndex.For([Term(1, "1 Test Left", "1 Teste Restante")]);

        Assert.Equal("1 Teste Restante", index.WholeStringMatch("  1 Test Left  ")?.Translation);
        Assert.Null(index.WholeStringMatch("1 Test Left over"));
    }

    /// <summary>A name is case-sensitive, and a lowercase word that merely spells it is not that name.</summary>
    [Fact]
    public void WholeStringMatch_CaseSensitiveTerm_DoesNotMatchDifferentCasing()
    {
        var index = GlossaryIndex.For([Term(1, "CAS", "CAS", neverTranslate: true, caseSensitive: true)]);

        Assert.NotNull(index.WholeStringMatch("CAS"));
        Assert.Null(index.WholeStringMatch("cas"));
    }

    [Fact]
    public void WholeStringMatch_CaseInsensitiveTerm_MatchesAnyCasing()
    {
        var index = GlossaryIndex.For([Term(1, "Body Selector", "Seletor de Corpo")]);

        Assert.Equal("Seletor de Corpo", index.WholeStringMatch("BODY SELECTOR")?.Translation);
    }

    /// <summary>
    /// The reason SelectForBatch exists. The old rule sorted the batch's matches by length and cut at the
    /// ceiling, so a string whose only rule was short lost it to other strings' longer ones.
    /// </summary>
    [Fact]
    public void SelectForBatch_AtTheCeiling_GivesEveryStringItsMostSpecificTermFirst()
    {
        var index = GlossaryIndex.For([
            Term(1, "A Very Long Terminology Rule", "Regra Longa"),
            Term(2, "Another Very Long Rule Here", "Outra Regra Longa"),
            Term(3, "Plug", "Plugue"),
        ]);

        var selected = index.SelectForBatch(
            ["A Very Long Terminology Rule appears here", "Another Very Long Rule Here too", "Use the Plug"],
            maximum: 2);

        // Two slots, three strings: the first round hands one to each string in turn, so the third
        // string's short rule is still ahead of any string's second-choice term.
        Assert.Equal(2, selected.Count);
        Assert.Equal(["A Very Long Terminology Rule", "Another Very Long Rule Here"], selected.Select(term => term.Original));

        var wider = index.SelectForBatch(
            ["A Very Long Terminology Rule appears here", "Another Very Long Rule Here too", "Use the Plug"],
            maximum: 3);
        Assert.Contains(wider, term => term.Original == "Plug");
    }

    [Fact]
    public void SelectForBatch_TermMatchingSeveralStrings_IsListedOnce()
    {
        var index = GlossaryIndex.For([Term(1, "Plug", "Plugue")]);

        var selected = index.SelectForBatch(["Use the Plug", "Remove the Plug"], maximum: 10);

        Assert.Single(selected);
    }

    /// <summary>Two terms may legitimately share an id when they come from different sources.</summary>
    [Fact]
    public void SelectForBatch_TermsSharingAnId_AreBothKept()
    {
        var index = GlossaryIndex.For([Term(1, "Plug", "Plugue"), Term(1, "Sim", "Sim")]);

        var selected = index.SelectForBatch(["Give the Sim a Plug"], maximum: 10);

        Assert.Equal(2, selected.Count);
    }

    [Fact]
    public void For_NoTerms_ReturnsAnIndexThatMatchesNothing()
    {
        Assert.Equal(0, GlossaryIndex.For(null).Count);
        Assert.Empty(GlossaryIndex.For([]).MatchesIn("anything"));
        Assert.Null(GlossaryIndex.For(null).WholeStringMatch("anything"));
    }

    /// <summary>A term with no word character cannot be filed, so it has to be checked against everything.</summary>
    [Fact]
    public void MatchesIn_TermWithNoWordCharacters_IsStillFound()
    {
        var index = GlossaryIndex.For([Term(1, "-->", "→")]);

        Assert.True(index.HasMatchIn("Press --> to continue"));
    }
}
