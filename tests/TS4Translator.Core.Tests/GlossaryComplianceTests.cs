using TS4Translator.Core.Glossary;

namespace TS4Translator.Core.Tests;

/// <summary>
/// The check that used to live inside the chat-window importer, where the engines the app drives itself
/// could never reach it.
/// </summary>
public class GlossaryComplianceTests
{
    private static GlossaryIndex Index(params GlossaryTerm[] terms) => GlossaryIndex.For(terms);

    private static GlossaryTerm Term(string original, string translation, bool neverTranslate = false, bool caseSensitive = false) =>
        new(1, original, translation, neverTranslate, caseSensitive, Notes: null, ProjectId: null);

    [Fact]
    public void Check_RequiredTranslationUsed_ReportsNothing()
    {
        var misses = GlossaryCompliance.Check(
            "Open the Body Selector", "Abra o Seletor de Corpo", Index(Term("Body Selector", "Seletor de Corpo")));

        Assert.Empty(misses);
    }

    [Fact]
    public void Check_RequiredTranslationIgnored_ReportsTheTerm()
    {
        var misses = GlossaryCompliance.Check(
            "Open the Body Selector", "Abra o Escolhedor de Corpo", Index(Term("Body Selector", "Seletor de Corpo")));

        var miss = Assert.Single(misses);
        Assert.Equal("Body Selector", miss.Term.Original);
        Assert.Contains("Seletor de Corpo", miss.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_NeverTranslateTermSurvived_ReportsNothing()
    {
        var misses = GlossaryCompliance.Check(
            "WickedWhims settings", "Configurações do WickedWhims",
            Index(Term("WickedWhims", "WickedWhims", neverTranslate: true, caseSensitive: true)));

        Assert.Empty(misses);
    }

    [Fact]
    public void Check_NeverTranslateTermWasTranslated_ReportsTheTerm()
    {
        var misses = GlossaryCompliance.Check(
            "WickedWhims settings", "Configurações dos Caprichos Perversos",
            Index(Term("WickedWhims", "WickedWhims", neverTranslate: true, caseSensitive: true)));

        var miss = Assert.Single(misses);
        Assert.Contains("sem traduzir", miss.Explanation, StringComparison.Ordinal);
    }

    /// <summary>Presence, not position: a term lands wherever the target language puts it.</summary>
    [Fact]
    public void Check_TermMovedWithinTheSentence_ReportsNothing()
    {
        var misses = GlossaryCompliance.Check(
            "The Body Selector is open", "Está aberto o Seletor de Corpo", Index(Term("Body Selector", "Seletor de Corpo")));

        Assert.Empty(misses);
    }

    [Fact]
    public void Check_TermDoesNotApplyToThisSource_IsNotChecked()
    {
        var misses = GlossaryCompliance.Check("Hello there", "Olá", Index(Term("Body Selector", "Seletor de Corpo")));

        Assert.Empty(misses);
    }

    [Fact]
    public void Check_NoGlossary_ReportsNothing()
    {
        Assert.Empty(GlossaryCompliance.Check("Anything", "Qualquer coisa", null));
        Assert.Empty(GlossaryCompliance.Check("Anything", "Qualquer coisa", GlossaryIndex.Empty));
    }

    [Fact]
    public void Summarize_SeveralMisses_JoinsThem()
    {
        var misses = GlossaryCompliance.Check(
            "Body Selector and WickedWhims", "Nada disso",
            Index(Term("Body Selector", "Seletor de Corpo"), Term("WickedWhims", "WickedWhims", neverTranslate: true)));

        Assert.Equal(2, misses.Count);
        Assert.Contains(";", GlossaryCompliance.Summarize(misses)!, StringComparison.Ordinal);
        Assert.Null(GlossaryCompliance.Summarize([]));
    }
}
