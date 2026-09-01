using TS4Translator.Providers.LocalModel;

namespace TS4Translator.Providers.Tests;

public sealed class ProtectedMarkersTests
{
    /// <summary>
    /// Seen on the real corpus: told a marker ends in two underscores, the model wrote four, and the
    /// restore glued a stray "__" onto the next word — "…reiniciar o jogo.\n\n__Salve e reinicie…".
    /// Nothing flagged it, because both markers were technically present.
    /// </summary>
    [Fact]
    public void Normalize_ExtraUnderscoresAfterAMarker_AreTrimmed()
        => Assert.Equal(
            "reiniciar o jogo.__TS4_TOKEN_0001__Salve e reinicie",
            ProtectedMarkers.Normalize("reiniciar o jogo.__TS4_TOKEN_0001____Salve e reinicie"));

    [Fact]
    public void Normalize_WellFormedMarker_IsLeftAlone()
        => Assert.Equal(
            "linha um__TS4_TOKEN_0001__linha dois",
            ProtectedMarkers.Normalize("linha um__TS4_TOKEN_0001__linha dois"));

    /// <summary>Two markers in a row are legitimate — "\n\n" protects as two — and must survive.</summary>
    [Fact]
    public void Normalize_TwoAdjacentMarkers_AreNotMerged()
        => Assert.Equal(
            "fim.__TS4_TOKEN_0001____TS4_TOKEN_0002__Começo",
            ProtectedMarkers.Normalize("fim.__TS4_TOKEN_0001____TS4_TOKEN_0002__Começo"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Normalize_NothingToDo_ReturnsEmpty(string? text)
        => Assert.Equal(string.Empty, ProtectedMarkers.Normalize(text));

    [Fact]
    public void Survived_EveryMarkerCameBack_IsTrue()
        => Assert.True(ProtectedMarkers.Survived(
            "A__TS4_TOKEN_0001__B__TS4_TOKEN_0002__C",
            "X__TS4_TOKEN_0002__Y__TS4_TOKEN_0001__Z"));

    /// <summary>The failure the retry pass exists for: two sentences merged, the line break dropped.</summary>
    [Fact]
    public void Survived_MarkerDropped_IsFalse()
        => Assert.False(ProtectedMarkers.Survived(
            "Check to enable.__TS4_TOKEN_0001__Uncheck to disable.",
            "Marque para ativar. Desmarque para desativar."));

    /// <summary>And the DEC-036 failure: a marker invented where the source had none.</summary>
    [Fact]
    public void Survived_MarkerInvented_IsFalse()
        => Assert.False(ProtectedMarkers.Survived(
            "clothing removal pantsfun 13",
            "Remoção de roupa: calça __TS4_TOKEN_0001__"));

    [Fact]
    public void Survived_NoMarkersEitherSide_IsTrue()
        => Assert.True(ProtectedMarkers.Survived("Shallow", "Raso"));

    [Fact]
    public void Survived_MarkerDuplicated_IsFalse()
        => Assert.False(ProtectedMarkers.Survived(
            "A__TS4_TOKEN_0001__B",
            "A__TS4_TOKEN_0001__B__TS4_TOKEN_0001__"));

    [Fact]
    public void Survived_NullTranslation_IsFalse()
        => Assert.False(ProtectedMarkers.Survived("A__TS4_TOKEN_0001__B", null));

    /// <summary>
    /// The failure that shipped: a 7B model answered a token-free string with the bare marker stem.
    /// Counting only well-formed markers made both sides zero, so nothing looked wrong and the retry
    /// never fired.
    /// </summary>
    [Theory]
    [InlineData("__TS4_TOKEN_")]
    [InlineData("__TS4_TOKEN")]
    [InlineData("__ts4_token_0001__")]
    [InlineData("* __TS4_TOKEN_I just have devious intentions.")]
    public void Survived_MangledMarkerInTheAnswer_IsFalse(string translation)
        => Assert.False(ProtectedMarkers.Survived("Ghost Unemotional", translation));

    /// <summary>Adjacent markers stay legitimate — a double line break protects as two.</summary>
    [Fact]
    public void Survived_TwoAdjacentMarkers_IsTrue()
        => Assert.True(ProtectedMarkers.Survived(
            "A__TS4_TOKEN_0001____TS4_TOKEN_0002__B",
            "X__TS4_TOKEN_0001____TS4_TOKEN_0002__Y"));
}
