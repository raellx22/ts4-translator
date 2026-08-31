using TS4Translator.Core.Memory;

namespace TS4Translator.Core.Tests;

public sealed class TranslationMemoryPolicyTests
{
    [Fact]
    public void ShouldRemember_ARealTranslation_IsTrue()
        => Assert.True(TranslationMemoryPolicy.ShouldRemember("Flogging for fun and profit.", "Açoitamento por diversão e lucro."));

    /// <summary>
    /// The failure this gate exists for. A chat model declined to translate one paragraph and returned it
    /// in English; it was stored as a finished translation. Memorising it would have taught the app that
    /// the English is the Portuguese, for every future mod carrying that line.
    /// </summary>
    [Fact]
    public void ShouldRemember_TranslationIdenticalToTheSource_IsFalse()
        => Assert.False(TranslationMemoryPolicy.ShouldRemember(
            "The sound of my paddle is one of my favorite pleasures.",
            "The sound of my paddle is one of my favorite pleasures."));

    [Fact]
    public void ShouldRemember_IgnoresSurroundingWhitespaceWhenComparing()
        => Assert.False(TranslationMemoryPolicy.ShouldRemember("Shallow", "  Shallow  "));

    [Theory]
    [InlineData("debug_savedActor2", "debug_savedActor2")]
    [InlineData("highWaistBelt", "cinto de cintura alta")]
    public void ShouldRemember_TuningIdentifier_IsFalse(string original, string translation)
        => Assert.False(TranslationMemoryPolicy.ShouldRemember(original, translation));

    [Theory]
    [InlineData(null, "Raso")]
    [InlineData("Shallow", null)]
    [InlineData("Shallow", "")]
    [InlineData("Shallow", "   ")]
    [InlineData("", "Raso")]
    public void ShouldRemember_MissingEitherSide_IsFalse(string? original, string? translation)
        => Assert.False(TranslationMemoryPolicy.ShouldRemember(original, translation));

    /// <summary>A short label that legitimately translates to something different is still worth keeping.</summary>
    [Fact]
    public void ShouldRemember_ShortTerm_IsTrue()
        => Assert.True(TranslationMemoryPolicy.ShouldRemember("Prayer Frame", "Estrutura de Oração"));
}
