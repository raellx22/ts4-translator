using System.Globalization;
using TS4Translator.Core.Interchange;
using TS4Translator.Core.Packages;

namespace TS4Translator.Core.Tests;

/// <summary>
/// A pack is how other people's translations travel, so the two things it must never lose are the text
/// itself — placeholders, line breaks and all — and the name of whoever wrote it.
/// </summary>
public sealed class TranslationPackFileTests : IDisposable
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    private readonly string _directory = Directory.CreateTempSubdirectory("ts4translator-pack-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_directory)) { Directory.Delete(_directory, recursive: true); }
    }

    private string Path(string name) => System.IO.Path.Combine(_directory, name);

    private static TranslationPack Pack(params AlignedTranslation[] pairs) =>
        new(English, PortugueseBrazil, DateTimeOffset.UtcNow, pairs);

    [Fact]
    public async Task WriteThenRead_RoundTripsEveryPairAndItsCredit()
    {
        var pack = Pack(
            new AlignedTranslation("Ask for Booty", "Pedir uma Transa", "AWE-EscortCareer_POR_BR"),
            new AlignedTranslation("Greet Sim", "Cumprimentar Sim", "LittleMsSam_PT"));
        var file = Path("pack.tsv");

        await TranslationPackFile.WriteAsync(file, pack);
        var readBack = await TranslationPackFile.ReadAsync(file);

        Assert.Equal(2, readBack.Pairs.Count);
        Assert.Contains(readBack.Pairs, p => p.OriginalText == "Ask for Booty" && p.SourcePackage == "AWE-EscortCareer_POR_BR");
        Assert.Equal(English.Name, readBack.SourceLocale.Name);
        Assert.Equal(PortugueseBrazil.Name, readBack.TargetLocale.Name);
    }

    /// <summary>
    /// A game string carries real newlines and the format is line-based — losing one would merge two
    /// sentences, which is exactly the damage this project spends most of its effort detecting.
    /// </summary>
    [Fact]
    public async Task WriteThenRead_PreservesNewlinesTabsAndPlaceholders()
    {
        const string original = "Check to enable.\nUncheck to disable.\tNow.";
        const string translated = "Marque para ativar.\n{M0.Garoto}{F0.Garota} de Programa\tAgora.";
        var file = Path("tricky.tsv");

        await TranslationPackFile.WriteAsync(file, Pack(new AlignedTranslation(original, translated, "mod_PT")));
        var readBack = await TranslationPackFile.ReadAsync(file);

        var pair = Assert.Single(readBack.Pairs);
        Assert.Equal(original, pair.OriginalText);
        Assert.Equal(translated, pair.TranslatedText);
    }

    [Fact]
    public async Task WriteThenRead_BackslashInTheTextSurvives()
    {
        const string translated = @"Pasta C:\Mods\ e depois \n literal";
        var file = Path("backslash.tsv");

        await TranslationPackFile.WriteAsync(file, Pack(new AlignedTranslation("A path", translated, "mod_PT")));
        var readBack = await TranslationPackFile.ReadAsync(file);

        Assert.Equal(translated, Assert.Single(readBack.Pairs).TranslatedText);
    }

    /// <summary>Credits are derived from the rows, so they cannot drift from what is actually inside.</summary>
    [Fact]
    public void Credits_ListEveryContributingPackageOnce()
    {
        var pack = Pack(
            new AlignedTranslation("A", "a", "tradutor-um"),
            new AlignedTranslation("B", "b", "tradutor-um"),
            new AlignedTranslation("C", "c", "tradutor-dois"),
            new AlignedTranslation("D", "d", string.Empty));

        Assert.Equal(["tradutor-dois", "tradutor-um"], pack.Credits);
    }

    [Fact]
    public async Task WriteAsync_PutsTheCreditsInTheFileHeaderForAnyoneOpeningIt()
    {
        var file = Path("credits.tsv");
        await TranslationPackFile.WriteAsync(file, Pack(new AlignedTranslation("A", "a", "tradutor-um")));

        var text = await File.ReadAllTextAsync(file);

        Assert.Contains("tradutor-um", text, StringComparison.Ordinal);
        Assert.Contains("Pacotes representados: 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_SomethingThatIsNotAPack_SaysSoInsteadOfReturningNonsense()
    {
        var file = Path("bundle.txt");
        await File.WriteAllTextAsync(file, "1\tQualquer coisa\tOutra coisa\n2\tMais\tCoisas");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => TranslationPackFile.ReadAsync(file));

        Assert.Contains("bundle.txt", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A hand-edited pack with a stray blank or malformed line must still load.</summary>
    [Fact]
    public async Task ReadAsync_MalformedRowsAmongGoodOnes_SkipsOnlyTheBadOnes()
    {
        var file = Path("edited.tsv");
        await TranslationPackFile.WriteAsync(file, Pack(new AlignedTranslation("Good", "Bom", "mod_PT")));
        await File.AppendAllTextAsync(file, "\n\nsó-uma-coluna\ndois\tcolunas\n");

        var readBack = await TranslationPackFile.ReadAsync(file);

        Assert.Equal("Good", Assert.Single(readBack.Pairs).OriginalText);
    }
}
