using TS4Translator.Core.Glossary;
using TS4Translator.Core.Translation;

namespace TS4Translator.Core.Tests;

public class EngineFreeAnswerTests
{
    private static GlossaryIndex Index(params GlossaryTerm[] terms) => GlossaryIndex.For(terms);

    private static GlossaryTerm Term(string original, string translation, bool neverTranslate = false, bool caseSensitive = false) =>
        new(1, original, translation, neverTranslate, caseSensitive, Notes: null, ProjectId: null);

    /// <summary>
    /// The identifiers from the import that motivated <see cref="StringTranslatability"/> — 128 of a
    /// thousand strings, every one of which the bulk run was paying an engine to look at.
    /// </summary>
    [Theory]
    [InlineData("debug_savedActor2")]
    [InlineData("craftGags1_cd")]
    [InlineData("publicUseCd")]
    [InlineData("var2")]
    public void For_TuningIdentifier_IsAnsweredWithItself(string identifier)
    {
        var answer = EngineFreeAnswer.For(identifier, GlossaryIndex.Empty);

        Assert.NotNull(answer);
        Assert.Equal(identifier, answer.Text);
        Assert.False(answer.FromGlossary);
        Assert.False(EngineFreeAnswer.NeedsEngine(identifier, GlossaryIndex.Empty));
    }

    [Fact]
    public void For_OrdinaryLabel_NeedsAnEngine()
    {
        Assert.Null(EngineFreeAnswer.For("Body Selector", GlossaryIndex.Empty));
        Assert.True(EngineFreeAnswer.NeedsEngine("Body Selector", GlossaryIndex.Empty));
    }

    [Fact]
    public void For_GlossaryCoversTheWholeString_AnswersFromTheGlossary()
    {
        var answer = EngineFreeAnswer.For("Body Selector", Index(Term("Body Selector", "Seletor de Corpo")));

        Assert.NotNull(answer);
        Assert.Equal("Seletor de Corpo", answer.Text);
        Assert.True(answer.FromGlossary);
    }

    /// <summary>A term inside a sentence is a rule for the engine, not an answer instead of one.</summary>
    [Fact]
    public void For_GlossaryCoversOnlyPartOfTheString_StillNeedsAnEngine()
    {
        Assert.Null(EngineFreeAnswer.For("Open the Body Selector now", Index(Term("Body Selector", "Seletor de Corpo"))));
    }

    /// <summary>"Never translate" over a whole string means the string is the name.</summary>
    [Fact]
    public void For_NeverTranslateTermCoveringTheString_KeepsTheModsOwnText()
    {
        var answer = EngineFreeAnswer.For("WickedWhims", Index(Term("WickedWhims", "WickedWhims", neverTranslate: true, caseSensitive: true)));

        Assert.NotNull(answer);
        Assert.Equal("WickedWhims", answer.Text);
        Assert.True(answer.FromGlossary);
    }

    [Fact]
    public void For_TermWithNoTranslation_FallsBackToTheEngine()
    {
        Assert.Null(EngineFreeAnswer.For("Body Selector", Index(Term("Body Selector", "   "))));
    }
}
