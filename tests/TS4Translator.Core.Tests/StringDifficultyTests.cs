using TS4Translator.Core.Providers;
using TS4Translator.Core.Translation;

namespace TS4Translator.Core.Tests;

public sealed class StringDifficultyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Score_EmptyText_IsZero(string? text) => Assert.Equal(0, StringDifficulty.Score(text));

    /// <summary>
    /// Real strings from the WickedWhims corpus. Short UI labels have to land below the default
    /// threshold - they are exactly what the cheap engine should be handling.
    /// </summary>
    [Theory]
    [InlineData("Shallow")]
    [InlineData("Average Hips")]
    [InlineData("Foot Model")]
    [InlineData("Weirdos Hangout")]
    [InlineData("Personality Impression")]
    public void Score_ShortUiLabel_StaysBelowTheDefaultThreshold(string text)
        => Assert.True(StringDifficulty.Score(text) < TranslationPipelineSettings.DefaultDifficultThreshold,
            $"\"{text}\" scored {StringDifficulty.Score(text)}");

    /// <summary>
    /// And the long, placeholder-carrying flavour text - where Argos actually lost the meaning - has to
    /// land above it.
    /// </summary>
    [Theory]
    [InlineData("Sharing a shower, and all the shower thoughts that come with it, gave {0.SimFirstName} some great ideas! They're ready to get creative, whether it's back in bed or a different outlet.")]
    [InlineData("{0.SimFirstName} caught a good whiff of another sim's smelly foot scent in the air. What wouldn't they do for the chance to taste their sweaty feet?")]
    public void Score_LongFlavourText_LandsAboveTheDefaultThreshold(string text)
        => Assert.True(StringDifficulty.Score(text) >= TranslationPipelineSettings.DefaultDifficultThreshold,
            $"\"{text[..30]}...\" scored {StringDifficulty.Score(text)}");

    /// <summary>
    /// Documents where the line actually falls, and that the score measures *structural* risk rather
    /// than semantic ambiguity. One plain sentence with no placeholders and no markup is mid-scoring
    /// even at ninety-odd characters — and in practice Argos did translate this one acceptably. The
    /// short strings it got wrong ("Shallow" as "Rasgo") are not caught by any shape-based score;
    /// that is what the review sweep and per-row refinement are for.
    /// </summary>
    [Fact]
    public void Score_OnePlainSentence_IsMidRangeEvenWhenLongish()
    {
        var score = StringDifficulty.Score("It's time to start coming clean about your foot fetish, no matter how embarrassing it can be.");

        Assert.InRange(score, 15, TranslationPipelineSettings.DefaultDifficultThreshold - 1);
    }

    [Fact]
    public void Score_PlaceholdersRaiseTheScore()
    {
        const string withoutPlaceholder = "The sim likes a clean-shaven face.";
        const string withPlaceholder = "{0.SimFirstName} likes a clean-shaven face.";

        Assert.True(StringDifficulty.Score(withPlaceholder) > StringDifficulty.Score(withoutPlaceholder));
    }

    [Fact]
    public void Score_GenderedVariantsRaiseTheScore()
    {
        const string plain = "{0.SimFirstName} dislikes them with average body weight.";
        const string gendered = "{0.SimFirstName} dislikes {M1.men}{F1.women} with average body weight.";

        Assert.True(StringDifficulty.Score(gendered) > StringDifficulty.Score(plain));
    }

    [Fact]
    public void Score_MarkupRaisesTheScore()
    {
        Assert.True(StringDifficulty.Score("<font color='#AB0C10'>Alert</font>") > StringDifficulty.Score("Alert"));
    }

    [Fact]
    public void Score_ProtectedTokenFormCountsAsAPlaceholder()
    {
        // The router runs before token protection in some paths and after it in others; both spellings
        // of a placeholder have to read as one.
        Assert.True(StringDifficulty.Score("__TS4_TOKEN_0001__ likes this.") > StringDifficulty.Score("Someone likes this."));
    }

    [Fact]
    public void Score_NeverLeavesTheZeroToHundredRange()
    {
        var extreme = string.Concat(Enumerable.Repeat("{0.SimFirstName} <b>x</b> is a very long sentence. ", 50));

        Assert.InRange(StringDifficulty.Score(extreme), 0, 100);
    }

    [Fact]
    public void IsDifficult_UsesThresholdInclusively()
    {
        var score = StringDifficulty.Score("Average Hips");

        Assert.True(StringDifficulty.IsDifficult("Average Hips", score));
        Assert.False(StringDifficulty.IsDifficult("Average Hips", score + 1));
    }

    /// <summary>
    /// The signal that catches what shape cannot: "Shallow" is a one-word label, but the app knows it
    /// already came back wrong once.
    /// </summary>
    [Fact]
    public void Score_PreviouslyFlaggedString_ClimbsWellAboveThePlainScore()
    {
        var plain = StringDifficulty.Score("Shallow");
        var flagged = StringDifficulty.Score("Shallow", new StringRiskContext(PreviouslyFlaggedForReview: true));

        Assert.True(plain < TranslationPipelineSettings.DefaultDifficultThreshold);
        Assert.True(flagged > plain);
    }

    [Fact]
    public void Score_GlossaryTermApplies_RaisesTheScore()
    {
        var plain = StringDifficulty.Score("Average Hips");
        var withTerm = StringDifficulty.Score("Average Hips", new StringRiskContext(HasGlossaryTerm: true));

        Assert.True(withTerm > plain);
    }

    /// <summary>
    /// A bare label has no context to disambiguate a word sense, so it is nudged up - but only nudged.
    /// Short labels are the bulk of a mod and must not all land on the expensive engine by shape alone.
    /// </summary>
    [Fact]
    public void Score_BareLabel_IsNudgedButStaysBelowTheDefaultThreshold()
    {
        var label = StringDifficulty.Score("Shallow");

        Assert.True(label > 0);
        Assert.True(label < TranslationPipelineSettings.DefaultDifficultThreshold);
    }

    [Fact]
    public void Score_RiskSignalsStack_ButNeverExceedTheRange()
    {
        var score = StringDifficulty.Score(
            string.Concat(Enumerable.Repeat("{0.SimFirstName} very long sentence here. ", 50)),
            new StringRiskContext(PreviouslyFlaggedForReview: true, HasGlossaryTerm: true));

        Assert.InRange(score, 0, 100);
    }
}
