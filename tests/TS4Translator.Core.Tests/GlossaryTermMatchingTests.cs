using TS4Translator.Core.Glossary;

namespace TS4Translator.Core.Tests;

/// <summary>
/// Guards the boundary rule in <see cref="GlossaryTerm.AppearsIn"/>. It only started mattering when the
/// glossary grew past proper nouns into ordinary words mined from the user's own approved translations —
/// at which point a substring match turns good rules into damage.
/// </summary>
public class GlossaryTermMatchingTests
{
    private static GlossaryTerm Term(string original, string translation = "traduzido", bool caseSensitive = false) =>
        new(0, original, translation, NeverTranslate: false, caseSensitive, Notes: null, ProjectId: null);

    /// <summary>
    /// The case that forced this: "Male → Masculino" is a good rule, and a substring match applies it to
    /// "Female" — telling the engine that Female is Masculino.
    /// </summary>
    [Fact]
    public void AppearsIn_TermInsideALongerWord_DoesNotMatch()
    {
        Assert.False(Term("Male").AppearsIn("Female Torso"));
        Assert.False(Term("Hard").AppearsIn("Hardcore Settings"));
        Assert.False(Term("Idle").AppearsIn("Idles"));
        Assert.False(Term("Sim").AppearsIn("Similar situations"));
    }

    [Fact]
    public void AppearsIn_TermAsAWholeWord_Matches()
    {
        Assert.True(Term("Male").AppearsIn("Male Torso"));
        Assert.True(Term("Male").AppearsIn("Human Male"));
        Assert.True(Term("Male").AppearsIn("Male"));
    }

    /// <summary>Punctuation and markup sit around labels constantly; they must not hide a term.</summary>
    [Theory]
    [InlineData("(Male)")]
    [InlineData("Male: choose one")]
    [InlineData("<b>Male</b>")]
    [InlineData("Bottom - Male")]
    [InlineData("Male/Female")]
    [InlineData("\"Male\"")]
    public void AppearsIn_TermNextToPunctuation_StillMatches(string text)
    {
        Assert.True(Term("Male").AppearsIn(text));
    }

    /// <summary>
    /// A tuning identifier is code, not a label — a glossary rule firing inside one would rewrite
    /// something the app deliberately never sends to a translator.
    /// </summary>
    [Fact]
    public void AppearsIn_InsideATuningIdentifier_DoesNotMatch()
    {
        Assert.False(Term("male").AppearsIn("male_torso_ref"));
        Assert.False(Term("actor").AppearsIn("debug_savedActor2"));
    }

    [Fact]
    public void AppearsIn_MultiWordTerm_MatchesOnBothEnds()
    {
        Assert.True(Term("Body Selector").AppearsIn("Open the Body Selector now"));
        Assert.False(Term("Body Selector").AppearsIn("Body Selectors"));
    }

    [Fact]
    public void AppearsIn_CaseSensitiveTerm_RespectsCase()
    {
        Assert.True(Term("CAS", caseSensitive: true).AppearsIn("Open CAS to continue"));
        Assert.False(Term("CAS", caseSensitive: true).AppearsIn("Open cas to continue"));
        Assert.True(Term("CAS", caseSensitive: false).AppearsIn("Open cas to continue"));
    }

    /// <summary>
    /// The first occurrence being embedded must not hide a real one later in the same string, which a
    /// naive "find once, check neighbours" implementation gets wrong.
    /// </summary>
    [Fact]
    public void AppearsIn_EmbeddedOccurrenceBeforeARealOne_StillMatches()
    {
        Assert.True(Term("Male").AppearsIn("Female and Male"));
    }
}
