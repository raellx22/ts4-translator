using System.Globalization;
using TS4Translator.Core.Packages;
using TS4Translator.Core.Projects;

namespace TS4Translator.Core.Tests;

public sealed class StringTableExportPlanTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    private const string ModA = "C:/Mods/ModA.package";
    private const string ModB = "C:/Mods/ModB.package";

    // The Sims 4 stores locale in the top byte of an STBL's 64-bit instance id; these fakes stand in
    // for SmartSimStringTableService so Core stays free of the DBPF library.
    private const ulong BaseInstance = 0x00_00E8D0AC1B9A96UL;
    private static StblKey KeyFor(byte localeByte, ulong baseInstance = BaseInstance) => new(Group: 0, ((ulong)localeByte << 56) | baseInstance);

    private static CultureInfo? LocaleOf(StblKey key) => (byte)(key.FullInstance >> 56) switch
    {
        0x00 => English,
        0x07 => French,
        0x11 => PortugueseBrazil,
        _ => null,
    };

    private static StblKey ToPortuguese(StblKey key) => KeyFor(0x11, key.FullInstance & 0x00_FFFFFFFFFFFFFFUL);

    private static ProjectStringEntry Entry(int id, string? sourceFile, StblKey stbl, uint keyHash, string original, string? translated) =>
        new(id, Guid.Empty, sourceFile, stbl, keyHash, original, translated, ProjectStringStatus.Translated, IssueSummary: null);

    [Fact]
    public void Build_MultipleLocaleTables_ExportsOnlyTheSourceLocale()
    {
        var english = KeyFor(0x00);
        var french = KeyFor(0x07);
        ProjectStringEntry[] entries =
        [
            Entry(1, ModA, english, 0xAAAA, "Shallow", "Raso"),
            Entry(2, ModA, french, 0xAAAA, "Superficiel", "Superficiel traduzido"),
        ];

        var plan = StringTableExportPlan.Build(entries, English, LocaleOf, ToPortuguese);

        var package = Assert.Single(plan.Packages);
        var table = Assert.Single(package.Tables);
        Assert.Equal(ToPortuguese(english), table.Key);
        Assert.Equal("Raso", Assert.Single(table.Value).Value);
        Assert.Equal(1, plan.ExcludedStringCount);
    }

    /// <summary>
    /// The real failure this class exists for: a project imported before the locale filter holds one
    /// copy of the mod per locale, and every one of those tables retargets onto the same pt-BR key.
    /// The old ToDictionary threw "An item with the same key has already been added".
    /// </summary>
    [Fact]
    public void Build_SourceTablesCollapsingOntoOneTargetKey_MergesInsteadOfThrowing()
    {
        var first = KeyFor(0x00);
        var second = KeyFor(0x01, BaseInstance);
        ProjectStringEntry[] entries =
        [
            Entry(1, ModA, first, 0xAAAA, "One", "Um"),
            Entry(2, ModA, second, 0xBBBB, "Two", "Dois"),
        ];

        var plan = StringTableExportPlan.Build(entries, English, _ => English, ToPortuguese);

        var package = Assert.Single(plan.Packages);
        Assert.Equal(2, Assert.Single(package.Tables).Value.Count);
        Assert.Equal(2, plan.StringCount);
        Assert.Equal(0, plan.ExcludedStringCount);
    }

    /// <summary>
    /// Importing twelve mods at once produced one merged package, which read as "only the first mod
    /// was translated". Each source `.package` has to come out as its own output.
    /// </summary>
    [Fact]
    public void Build_SeveralSourceMods_ProducesOnePackagePerMod()
    {
        var english = KeyFor(0x00);
        ProjectStringEntry[] entries =
        [
            Entry(1, ModA, english, 0xAAAA, "One", "Um"),
            Entry(2, ModB, english, 0xBBBB, "Two", "Dois"),
        ];

        var plan = StringTableExportPlan.Build(entries, English, LocaleOf, ToPortuguese);

        Assert.Equal(2, plan.Packages.Count);
        Assert.Equal(["ModA", "ModB"], plan.Packages.Select(p => p.SourceModName));
        Assert.All(plan.Packages, package => Assert.Equal(1, package.StringCount));
    }

    /// <summary>
    /// Two unrelated mods may ship an STBL with the very same group and instance. They must stay in
    /// separate output packages instead of being merged into one.
    /// </summary>
    [Fact]
    public void Build_TwoModsSharingAnStblKey_KeepsThemInSeparatePackages()
    {
        var english = KeyFor(0x00);
        ProjectStringEntry[] entries =
        [
            Entry(1, ModA, english, 0xAAAA, "From A", "De A"),
            Entry(2, ModB, english, 0xAAAA, "From B", "De B"),
        ];

        var plan = StringTableExportPlan.Build(entries, English, LocaleOf, ToPortuguese);

        Assert.Equal(2, plan.Packages.Count);
        Assert.Equal("De A", Assert.Single(Assert.Single(plan.Packages[0].Tables).Value).Value);
        Assert.Equal("De B", Assert.Single(Assert.Single(plan.Packages[1].Tables).Value).Value);
    }

    [Fact]
    public void Build_RowsWithoutASourceFile_ShareOneUnnamedPackage()
    {
        var english = KeyFor(0x00);
        ProjectStringEntry[] entries =
        [
            Entry(1, sourceFile: null, english, 0xAAAA, "One", "Um"),
            Entry(2, sourceFile: null, english, 0xBBBB, "Two", "Dois"),
        ];

        var plan = StringTableExportPlan.Build(entries, English, LocaleOf, ToPortuguese);

        var package = Assert.Single(plan.Packages);
        Assert.Null(package.SourceModName);
        Assert.Equal(2, package.StringCount);
    }

    [Fact]
    public void Build_UntranslatedString_FallsBackToTheOriginalText()
    {
        ProjectStringEntry[] entries = [Entry(1, ModA, KeyFor(0x00), 0xAAAA, "Average Hips", translated: null)];

        var plan = StringTableExportPlan.Build(entries, English, LocaleOf, ToPortuguese);

        Assert.Equal("Average Hips", Assert.Single(Assert.Single(Assert.Single(plan.Packages).Tables).Value).Value);
    }

    [Fact]
    public void Build_NoSourceLocaleStringsAtAll_ExportsEverythingRatherThanNothing()
    {
        ProjectStringEntry[] entries = [Entry(1, ModA, KeyFor(0x07), 0xAAAA, "Superficiel", "Raso")];

        var plan = StringTableExportPlan.Build(entries, English, LocaleOf, ToPortuguese);

        Assert.Equal(1, plan.StringCount);
        Assert.Equal(0, plan.ExcludedStringCount);
    }

    [Fact]
    public void Build_TableWithNoRecognizedLocale_IsNotTreatedAsSourceText()
    {
        var english = KeyFor(0x00);
        ProjectStringEntry[] entries =
        [
            Entry(1, ModA, english, 0xAAAA, "Shallow", "Raso"),
            Entry(2, ModA, KeyFor(0x7F), 0xBBBB, "???", "???"),
        ];

        var plan = StringTableExportPlan.Build(entries, English, LocaleOf, ToPortuguese);

        Assert.Equal(1, plan.StringCount);
        Assert.Equal(1, plan.ExcludedStringCount);
    }
}
