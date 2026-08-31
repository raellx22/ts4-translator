using TS4Translator.Core.Translation;

namespace TS4Translator.Core.Tests;

public sealed class StringTranslatabilityTests
{
    /// <summary>
    /// Every one of these is a real string from "[Kritical] Dreams Of Surrender". They live in the mod's
    /// string table, they are never shown to a player, and sending them to a translator produced 128 of
    /// the 134 warnings on a thousand-string import.
    /// </summary>
    [Theory]
    [InlineData("debug_savedActor2")]
    [InlineData("debug_interactionContext")]
    [InlineData("highWaistBelt")]
    [InlineData("publicUseCd")]
    [InlineData("craftGags1_cd")]
    [InlineData("suspensionPost_lockin_obj")]
    [InlineData("etiquette_dom_succ_rebake")]
    [InlineData("standcarryspank_s_fail")]
    [InlineData("crosscombo_cross_whipping1_1_o")]
    [InlineData("upgradeProgress_2")]
    [InlineData("var2")]
    [InlineData("l1")]
    [InlineData("tooltipUpdater")]
    [InlineData("milkValue_activeUse")]
    public void Classify_TuningIdentifier_IsCode(string text)
        => Assert.Equal(StringKind.Code, StringTranslatability.Classify(text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("42")]
    [InlineData("---")]
    [InlineData("{0.SimFirstName}")]
    [InlineData("<new string>")]
    public void Classify_NothingToTranslate_IsCode(string text)
        => Assert.Equal(StringKind.Code, StringTranslatability.Classify(text));

    /// <summary>
    /// Labels and names. They can legitimately come back from a translator unchanged, so an unchanged one
    /// is worth a note and never a warning — but they are still text, so they must still be sent.
    /// </summary>
    [Theory]
    [InlineData("Kritical")]
    [InlineData("Flogger")]
    [InlineData("Inspection")]
    [InlineData("Prayer Frame")]
    [InlineData("[Kritical] Glass Plug")]
    [InlineData("Lock Inside (Punishment)")]
    [InlineData("chastity low trained")]
    [InlineData("notif cd")]
    public void Classify_ShortLabel_IsATerm(string text)
        => Assert.Equal(StringKind.Term, StringTranslatability.Classify(text));

    [Theory]
    [InlineData("Flogging for fun and profit.")]
    [InlineData("No Interactions Available?")]
    [InlineData("It might weight a ton, but dont worry.")]
    [InlineData("<i>The sound of my paddle is one of my favorite pleasures.</i>")]
    [InlineData("{0.SimFirstName} looks around the room and decides to wait")]
    public void Classify_Sentence_IsProse(string text)
        => Assert.Equal(StringKind.Prose, StringTranslatability.Classify(text));

    /// <summary>
    /// The asymmetry that drives the whole heuristic: a needless note costs a glance, a string wrongly
    /// withheld ships untranslated. So anything with a real word in it goes to the translator.
    /// </summary>
    [Theory]
    [InlineData("Kritical")]
    [InlineData("Prayer Frame")]
    [InlineData("Flogging for fun and profit.")]
    [InlineData("notif cd")]
    public void IsWorthTranslating_AnythingWithRealWords_IsTrue(string text)
        => Assert.True(StringTranslatability.IsWorthTranslating(text));

    [Theory]
    [InlineData("debug_savedActor2")]
    [InlineData("highWaistBelt")]
    [InlineData("{0.SimFirstName}")]
    public void IsWorthTranslating_Identifier_IsFalse(string text)
        => Assert.False(StringTranslatability.IsWorthTranslating(text));
}
