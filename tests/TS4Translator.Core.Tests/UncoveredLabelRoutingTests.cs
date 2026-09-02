using TS4Translator.Core.Providers;
using TS4Translator.Core.Translation;

namespace TS4Translator.Core.Tests;

/// <summary>
/// Guards the routing signal added after measuring where a weak engine actually fails.
/// </summary>
/// <remarks>
/// Hy-MT2-1.8B handed back 26 of 227 strings untranslated across eight unseen mods, and every one was a
/// bare label — "Twink", "Mellow", "Cheese Toastie". They averaged 14,1 points against 24,0 for the
/// strings it translated fine, so the tiered routing sent all 26 to the cheap engine and none to the
/// good one. These tests pin the correction: an unexplained label now outranks the prose that was
/// getting promoted ahead of it.
/// </remarks>
public class UncoveredLabelRoutingTests
{
    private const int DefaultThreshold = TranslationPipelineSettings.DefaultDifficultThreshold;

    [Theory]
    [InlineData("Twink")]
    [InlineData("Mellow")]
    [InlineData("Cheese Toastie")]
    [InlineData("Muscle Bear")]
    [InlineData("Comfort Seeker")]
    public void Score_BareLabelNobodyCanExplain_ReachesTheGoodEngine(string label)
    {
        var score = StringDifficulty.Score(label, new StringRiskContext(IsUncoveredLabel: true));

        Assert.True(score >= DefaultThreshold, $"\"{label}\" pontuou {score}, abaixo do limiar {DefaultThreshold}");
    }

    /// <summary>
    /// The other half of the trade. Short labels are most of a mod — 55 thousand of WickedWhims' 149
    /// thousand — so promoting them all would send the whole job to the expensive engine. A label the
    /// glossary covers, or that this project already translated, stays cheap.
    /// </summary>
    [Theory]
    [InlineData("Blue Eyes")]
    [InlineData("Cheese Toastie")]
    [InlineData("Muscular")]
    public void Score_LabelTheAppCanExplain_StaysOnTheCheapEngine(string label)
    {
        var score = StringDifficulty.Score(label, new StringRiskContext(IsUncoveredLabel: false));

        Assert.True(score < DefaultThreshold, $"\"{label}\" pontuou {score} sem motivo para subir de faixa");
    }

    /// <summary>The signal must not fire on prose — that is not the failure it was measured for.</summary>
    [Fact]
    public void IsBareLabel_Prose_IsNotALabel()
    {
        Assert.False(StringDifficulty.IsBareLabel("Are you sure you want to do this right now?"));
        Assert.False(StringDifficulty.IsBareLabel("Ask {0.SimFirstName} to stay the night, if they want to."));
        Assert.False(StringDifficulty.IsBareLabel(null));
    }

    [Theory]
    [InlineData("Twink")]
    [InlineData("Blue Eyes")]
    [InlineData("Comfort Seeker")]
    public void IsBareLabel_ShortLabel_IsALabel(string label)
    {
        Assert.True(StringDifficulty.IsBareLabel(label));
    }

    /// <summary>
    /// The inversion itself, as a regression test: an unexplained label must now outrank the long
    /// placeholder-carrying sentence that the old score promoted ahead of it.
    /// </summary>
    [Fact]
    public void Score_UnexplainedLabel_OutranksProseTheEngineHandlesFine()
    {
        var label = StringDifficulty.Score("Twink", new StringRiskContext(IsUncoveredLabel: true));
        var prose = StringDifficulty.Score(
            "Set a unit interval value for chance of random sex occurring when travelling.", new StringRiskContext());

        Assert.True(label > prose, $"rótulo {label} não deveria pontuar abaixo da prosa {prose}");
    }
}
