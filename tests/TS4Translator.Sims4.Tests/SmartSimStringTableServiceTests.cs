using System.Globalization;
using TS4Translator.Sims4;

namespace TS4Translator.Sims4.Tests;

public class SmartSimStringTableServiceTests
{
    private readonly SmartSimStringTableService _sut = new();

    private static readonly CultureInfo EnglishUs = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly CultureInfo FrenchFrance = CultureInfo.GetCultureInfo("fr-FR");

    [Fact]
    public void CreateKey_DifferentLocales_ProduceDifferentKeysForSameGroupAndInstance()
    {
        const uint group = 0;
        const ulong instance = 0x0011223344UL;

        var englishKey = _sut.CreateKey(EnglishUs, group, instance);
        var portugueseKey = _sut.CreateKey(PortugueseBrazil, group, instance);

        Assert.NotEqual(englishKey, portugueseKey);
        Assert.Equal(englishKey.Group, portugueseKey.Group);
    }

    [Fact]
    public void CreateKey_ThenGetLocale_RoundTripsForEnglish()
    {
        var key = _sut.CreateKey(EnglishUs, group: 0, instance: 0x0011223344UL);

        var locale = _sut.GetLocale(key);

        Assert.Equal(EnglishUs, locale);
    }

    [Fact]
    public void CreateKey_ThenGetLocale_RoundTripsForPortugueseBrazil()
    {
        var key = _sut.CreateKey(PortugueseBrazil, group: 0, instance: 0x0011223344UL);

        var locale = _sut.GetLocale(key);

        Assert.Equal(PortugueseBrazil, locale);
    }

    [Fact]
    public void WithLocale_RetargetsKeyToRequestedLocale_KeepingGroupAndBaseInstance()
    {
        var englishKey = _sut.CreateKey(EnglishUs, group: 7, instance: 0x00AABBCCDDUL);

        var portugueseKey = _sut.WithLocale(englishKey, PortugueseBrazil);

        Assert.Equal(englishKey.Group, portugueseKey.Group);
        Assert.Equal(PortugueseBrazil, _sut.GetLocale(portugueseKey));
        Assert.Equal(EnglishUs, _sut.GetLocale(englishKey));
    }

    [Fact]
    public void WithLocale_AppliedTwiceForSameLocale_IsIdempotent()
    {
        var key = _sut.CreateKey(FrenchFrance, group: 0, instance: 0x1UL);

        var retargeted = _sut.WithLocale(key, FrenchFrance);

        Assert.Equal(key, retargeted);
    }

    [Fact]
    public void CreateKey_LocaleNotSupportedByTheSims4_ThrowsNotSupportedException()
    {
        // LlamaLogic.Packages' own XML doc comment claims ArgumentOutOfRangeException here, but the
        // installed 3.8.2 binary actually throws NotSupportedException — verified empirically rather
        // than trusting the doc comment (see docs/DECISIONS.md).
        var unsupportedLocale = CultureInfo.GetCultureInfo("ar-SA");

        Assert.Throws<NotSupportedException>(() => _sut.CreateKey(unsupportedLocale, group: 0, instance: 0x1UL));
    }
}
