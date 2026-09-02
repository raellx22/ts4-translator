using TS4Translator.Core.Translation;

namespace TS4Translator.Core.Tests;

/// <summary>
/// Every "damaged" case here is a real row from the user's own database — a translation the app had
/// stored as finished work, with nothing anywhere pointing at it. Every "fine" case is a real row too,
/// and they are the harder half: the detector is worth nothing if the queue it produces is mostly good
/// translations that Portuguese happened to render more tersely than English.
/// </summary>
public class TranslationSuspicionTests
{
    /// <summary>The user's own example: the game concept dropped and the label flattened to an adjective.</summary>
    [Fact]
    public void Assess_LabelThatLostTheGameTerm_IsSuspect()
    {
        var assessment = TranslationSuspicion.Assess("Attractiveness Tuner", "Atraente");

        Assert.True(assessment.IsSuspect);
        Assert.Contains(SuspicionReason.LostWordsInLabel, assessment.Reasons);
    }

    [Fact]
    public void Assess_SecondSentenceMissing_IsSuspect()
    {
        var assessment = TranslationSuspicion.Assess(
            "I have something for you to feel :wink: I'm coming over!", "Tenho algo para você sentir:");

        Assert.True(assessment.IsSuspect);
    }

    [Fact]
    public void Assess_ThreeClausesCollapsedIntoOne_IsSuspect()
    {
        var assessment = TranslationSuspicion.Assess("Ugh... haha, sure... let's do it!", "Vamos lá!");

        Assert.True(assessment.IsSuspect);
        Assert.Contains(SuspicionReason.LostSentences, assessment.Reasons);
    }

    /// <summary>
    /// Around three thousand rows of this shape in one mod. The heading's rule is the mod author's own
    /// formatting and the engine replaced it with a stray dash.
    /// </summary>
    [Fact]
    public void Assess_SectionHeadingThatLostItsRule_IsSuspect()
    {
        var assessment = TranslationSuspicion.Assess("---------- CUM INTERACTIONS ----------", "- Interações comuns...");

        Assert.True(assessment.IsSuspect);
        Assert.Contains(SuspicionReason.LostStructure, assessment.Reasons);
    }

    /// <summary>Both numbers matter, and both are the digit "1" — so this has to be counted, not matched.</summary>
    [Fact]
    public void Assess_RepeatedDigitDroppedOnce_IsSuspect()
    {
        var assessment = TranslationSuspicion.Assess("1 like = 1 day of not jerking off.", "1 dia sem bater uma.");

        Assert.True(assessment.IsSuspect);
        Assert.Contains(SuspicionReason.LostNumbers, assessment.Reasons);
    }

    [Fact]
    public void Assess_ProseThatCameBackInEnglish_IsSuspect()
    {
        var assessment = TranslationSuspicion.Assess(
            "Are you sure you want to do this right now?", "Are you sure you want to do this right now?");

        Assert.True(assessment.IsSuspect);
        Assert.Contains(SuspicionReason.IdenticalToSource, assessment.Reasons);
    }

    // ---- The other half: real rows that are fine and must stay out of the queue ----

    [Theory]
    [InlineData("Run Away", "Fuja!")]
    [InlineData("Go Back", "Volte.")]
    [InlineData("Ask for Sex", "Peça sexo")]
    [InlineData("Sex Tuner", "Ajustador de Sexo")]
    [InlineData("Congratulations for the baby!", "Parabéns pelo bebê!")]
    [InlineData("I will pick it up some other day.", "Vou buscá-lo outro dia.")]
    [InlineData("Isn't it a little too soon to be talking about kids?", "Não é cedo demais para falar de crianças?")]
    [InlineData("Erectile Dysfunction Consultation", "Consulta de Disfunção Erétil")]
    [InlineData("1PM (13:00)", "13h00")]
    [InlineData("Native / Override", "Nativo/Substituir")]
    public void Assess_GoodTranslation_IsNotSuspect(string original, string translated)
    {
        Assert.False(TranslationSuspicion.Assess(original, translated).IsSuspect);
    }

    /// <summary>
    /// A tuning identifier coming back unchanged is the correct answer (see
    /// <see cref="StringTranslatability"/>), and reading it as total loss would bury the real findings
    /// under a fifth of the mod.
    /// </summary>
    [Theory]
    [InlineData("debug_savedActor2")]
    [InlineData("craftGags1_cd")]
    [InlineData("publicUseCd")]
    public void Assess_TuningIdentifierLeftAlone_IsNotSuspect(string identifier)
    {
        Assert.Equal(SuspicionAssessment.None, TranslationSuspicion.Assess(identifier, identifier));
    }

    /// <summary>
    /// A short term coming back identical is an observation, not an alarm - "Strip Poker!" is "Strip
    /// Poker!". Only prose returning in English is a finding.
    /// </summary>
    [Fact]
    public void Assess_TermThatLegitimatelyStaysInEnglish_IsNotSuspect()
    {
        Assert.False(TranslationSuspicion.Assess("Strip Poker!", "Strip Poker!").IsSuspect);
    }

    /// <summary>
    /// Placeholders and markup are copied verbatim, so they must not count towards length on either
    /// side: a short label wrapped in a font tag is mostly tag, and the ratio would be meaningless.
    /// </summary>
    [Fact]
    public void Assess_MarkupAndPlaceholdersDoNotDistortTheComparison()
    {
        var assessment = TranslationSuspicion.Assess(
            "<font color='#510061'>Ask {0.SimFirstName} to stay the night</font>",
            "<font color='#510061'>Pedir para {0.SimFirstName} passar a noite</font>");

        Assert.False(assessment.IsSuspect);
    }

    /// <summary>A hex colour is not a separator run; only three or more of the same character is.</summary>
    [Fact]
    public void Assess_HexColourIsNotReadAsLostStructure()
    {
        var assessment = TranslationSuspicion.Assess(
            "<font color='#510061'>Blushing Bashfulness</font>", "<font color='#510061'>Timidez Corada</font>");

        Assert.DoesNotContain(SuspicionReason.LostStructure, assessment.Reasons);
    }

    [Fact]
    public void Assess_NotTranslatedYet_IsNotSuspect()
    {
        Assert.Equal(SuspicionAssessment.None, TranslationSuspicion.Assess("Anything at all", null));
        Assert.Equal(SuspicionAssessment.None, TranslationSuspicion.Assess("Anything at all", "   "));
    }

    /// <summary>Several things missing at once should outrank one, so the worst rows sort to the top.</summary>
    [Fact]
    public void Assess_SeveralFindings_ScoresHigherThanOne()
    {
        var one = TranslationSuspicion.Assess("Ask for Sex Right Now", "Peça");
        var several = TranslationSuspicion.Assess("---------- SEX PREFERENCES ----------", "-Preferências sexuais...");

        Assert.True(several.Score > one.Score);
        Assert.True(several.Reasons.Count > 1);
    }

    /// <summary>Short strings are excluded from the ratio rule: one word swings it and it means nothing.</summary>
    [Fact]
    public void Assess_VeryShortSource_DoesNotFireTheLengthRule()
    {
        var assessment = TranslationSuspicion.Assess("Tipsy", "Bêbado");

        Assert.DoesNotContain(SuspicionReason.SeverelyShortened, assessment.Reasons);
    }
}
