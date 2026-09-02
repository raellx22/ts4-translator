using System.Globalization;
using TS4Translator.Core.Packages;

namespace TS4Translator.Sims4.Tests;

/// <summary>
/// Round-trips through real packages written here — synthetic and non-copyrighted, per brief section 44.
/// </summary>
public sealed class AlignedTranslationHarvesterTests : IDisposable
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    private readonly Sims4PackageService _packages = new();
    private readonly SmartSimStringTableService _locales = new();
    private readonly AlignedTranslationHarvester _sut;

    private readonly string _mods = Directory.CreateTempSubdirectory("ts4translator-harvest-mods-").FullName;
    private readonly string _translations = Directory.CreateTempSubdirectory("ts4translator-harvest-tr-").FullName;

    public AlignedTranslationHarvesterTests() => _sut = new AlignedTranslationHarvester(_packages, _locales);

    public void Dispose()
    {
        foreach (var folder in new[] { _mods, _translations })
        {
            if (Directory.Exists(folder)) { Directory.Delete(folder, recursive: true); }
        }
    }

    private async Task WriteAsync(string folder, string name, CultureInfo locale, params (uint Hash, string Text)[] entries)
    {
        var key = _locales.CreateKey(locale, group: 0x1234, instance: 0x0011223344556677UL);
        await _packages.WriteStringTableAsync(
            Path.Combine(folder, name), key, [.. entries.Select(e => new StringTableEntry(e.Hash, e.Text))]);
    }

    /// <summary>
    /// The shape this exists for: a mod in English, and a separate community package holding only the
    /// Portuguese, paired by the hash they share.
    /// </summary>
    [Fact]
    public async Task HarvestAsync_TranslationPackageForAMod_PairsThemByKeyHash()
    {
        await WriteAsync(_mods, "mod.package", English, (0xAAAA0001, "Ask for Booty"), (0xAAAA0002, "Escort Menu"));
        await WriteAsync(_translations, "mod-ptbr.package", PortugueseBrazil, (0xAAAA0001, "Pedir uma Transa"), (0xAAAA0002, "Menu de Acompanhante"));

        var harvest = await _sut.HarvestAsync([_mods], [_translations], English, PortugueseBrazil);

        Assert.Equal(2, harvest.Pairs.Count);
        Assert.Contains(harvest.Pairs, p => p.OriginalText == "Ask for Booty" && p.TranslatedText == "Pedir uma Transa");
        Assert.Contains(harvest.Pairs, p => p.OriginalText == "Escort Menu" && p.TranslatedText == "Menu de Acompanhante");
    }

    /// <summary>A mod shipping both locales in one file pairs with itself — 83 of the library did.</summary>
    [Fact]
    public async Task HarvestAsync_ModShippingBothLocales_PairsAgainstItself()
    {
        await WriteAsync(_mods, "bilingual-en.package", English, (0xBBBB0001, "Wild Ride"));
        await WriteAsync(_mods, "bilingual-pt.package", PortugueseBrazil, (0xBBBB0001, "Passeio Selvagem"));

        var harvest = await _sut.HarvestAsync([_mods], [_mods], English, PortugueseBrazil);

        var pair = Assert.Single(harvest.Pairs);
        Assert.Equal("Passeio Selvagem", pair.TranslatedText);
    }

    /// <summary>
    /// The guard that keeps this trustworthy: one hash carrying different text in two mods cannot be
    /// paired without guessing, so it is refused and counted rather than resolved.
    /// </summary>
    [Fact]
    public async Task HarvestAsync_SameHashMeaningTwoThingsInTwoMods_IsRefused()
    {
        await WriteAsync(_mods, "mod-a.package", English, (0xCCCC0001, "Stock"));
        await WriteAsync(_mods, "mod-b.package", English, (0xCCCC0001, "Idle"));
        await WriteAsync(_translations, "tr.package", PortugueseBrazil, (0xCCCC0001, "Tronco"));

        var harvest = await _sut.HarvestAsync([_mods], [_translations], English, PortugueseBrazil);

        Assert.Empty(harvest.Pairs);
        Assert.Equal(1, harvest.AmbiguousKeys);
    }

    /// <summary>
    /// A translation package is often incomplete: the entries the translator had not reached still hold
    /// the English. Storing those would teach the app that the English is the Portuguese.
    /// </summary>
    [Fact]
    public async Task HarvestAsync_EntryStillInEnglish_IsNotAPair()
    {
        await WriteAsync(_mods, "mod.package", English, (0xDDDD0001, "Toastie"), (0xDDDD0002, "Cheese Toastie"));
        await WriteAsync(_translations, "tr.package", PortugueseBrazil, (0xDDDD0001, "Toastie"), (0xDDDD0002, "Sanduíche de Queijo"));

        var harvest = await _sut.HarvestAsync([_mods], [_translations], English, PortugueseBrazil);

        var pair = Assert.Single(harvest.Pairs);
        Assert.Equal("Cheese Toastie", pair.OriginalText);
    }

    [Fact]
    public async Task HarvestAsync_TranslationWithNoMatchingSource_IsSkipped()
    {
        await WriteAsync(_mods, "mod.package", English, (0xEEEE0001, "Greet Sim"));
        await WriteAsync(_translations, "tr.package", PortugueseBrazil, (0xEEEE0009, "Órfã"));

        var harvest = await _sut.HarvestAsync([_mods], [_translations], English, PortugueseBrazil);

        Assert.Empty(harvest.Pairs);
        Assert.Equal(0, harvest.AmbiguousKeys);
    }

    /// <summary>
    /// A library is mostly meshes and textures, and a corrupt file is normal. One bad package must not
    /// abort a harvest of thousands.
    /// </summary>
    [Fact]
    public async Task HarvestAsync_UnreadableFileAmongGoodOnes_DoesNotStopTheHarvest()
    {
        await WriteAsync(_mods, "mod.package", English, (0xFFFF0001, "Spot Dance"));
        await WriteAsync(_translations, "tr.package", PortugueseBrazil, (0xFFFF0001, "Dança no Lugar"));
        await File.WriteAllTextAsync(Path.Combine(_mods, "broken.package"), "isto não é um DBPF");

        var harvest = await _sut.HarvestAsync([_mods], [_translations], English, PortugueseBrazil);

        Assert.Single(harvest.Pairs);
    }

    [Fact]
    public async Task HarvestAsync_NoFoldersThatExist_ReturnsAnEmptyHarvest()
    {
        var harvest = await _sut.HarvestAsync(
            [Path.Combine(_mods, "não-existe")], [Path.Combine(_translations, "também-não")], English, PortugueseBrazil);

        Assert.Empty(harvest.Pairs);
        Assert.Equal(0, harvest.PackagesScanned);
    }

    /// <summary>Placeholders and gendered variants are the point — they must survive verbatim.</summary>
    [Fact]
    public async Task HarvestAsync_TranslationCarryingTokens_KeepsThemExactly()
    {
        const string translated = "{M0.Garoto}{F0.Garota} de Programa ganhou {4.Money}";
        await WriteAsync(_mods, "mod.package", English, (0x12340001, "Streetwalker earned {4.Money}"));
        await WriteAsync(_translations, "tr.package", PortugueseBrazil, (0x12340001, translated));

        var harvest = await _sut.HarvestAsync([_mods], [_translations], English, PortugueseBrazil);

        Assert.Equal(translated, Assert.Single(harvest.Pairs).TranslatedText);
    }
}
