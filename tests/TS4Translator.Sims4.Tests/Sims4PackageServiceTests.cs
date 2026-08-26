using System.Globalization;
using TS4Translator.Core.Packages;
using TS4Translator.Sims4;

namespace TS4Translator.Sims4.Tests;

/// <summary>
/// Round-trip tests against synthetic, non-copyrighted packages only (brief section 44: no
/// third-party mod content in the repository).
/// </summary>
public sealed class Sims4PackageServiceTests : IDisposable
{
    private readonly Sims4PackageService _sut = new();
    private readonly SmartSimStringTableService _localeService = new();
    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("ts4translator-tests-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string OutputPath(string fileName) => Path.Combine(_tempDirectory, fileName);

    [Fact]
    public async Task WriteStringTableAsync_ThenReadStringTableAsync_PreservesKeyHashesAndValues()
    {
        var key = new StblKey(Group: 0, FullInstance: 0x0011223344556677UL);
        var entries = new[]
        {
            new StringTableEntry(0x91447A25, "Ask {0.SimFirstName} about their relationship."),
            new StringTableEntry(0xDEADBEEF, "WooHoo!"),
            new StringTableEntry(0x00000001, string.Empty),
        };
        var outputPath = OutputPath("roundtrip.package");

        await _sut.WriteStringTableAsync(outputPath, key, entries);
        var readBack = await _sut.ReadStringTableAsync(outputPath, key);

        Assert.Equal(entries.Length, readBack.Count);
        foreach (var expected in entries)
        {
            var actual = Assert.Single(readBack, e => e.KeyHash == expected.KeyHash);
            Assert.Equal(expected.Value, actual.Value);
        }
    }

    [Fact]
    public async Task WriteStringTableAsync_ThenFindStringTablesAsync_LocatesTheWrittenKey()
    {
        var key = new StblKey(Group: 42, FullInstance: 0xABCDEFUL);
        var outputPath = OutputPath("find.package");

        await _sut.WriteStringTableAsync(outputPath, key, [new StringTableEntry(1, "hello")]);
        var found = await _sut.FindStringTablesAsync(outputPath);

        var foundKey = Assert.Single(found);
        Assert.Equal(key, foundKey);
    }

    [Fact]
    public async Task WriteStringTableAsync_LocaleSpecificKey_RoundTripsWithCorrectLocale()
    {
        var portuguese = CultureInfo.GetCultureInfo("pt-BR");
        var key = _localeService.CreateKey(portuguese, group: 0, instance: 0x42UL);
        var outputPath = OutputPath("locale.package");

        await _sut.WriteStringTableAsync(outputPath, key, [new StringTableEntry(1, "Oba-oba!")]);
        var found = await _sut.FindStringTablesAsync(outputPath);
        var foundKey = Assert.Single(found);

        Assert.Equal(portuguese, _localeService.GetLocale(foundKey));
    }

    [Fact]
    public async Task WriteStringTableAsync_OutputAlreadyExists_ThrowsAndDoesNotOverwrite()
    {
        var key = new StblKey(Group: 0, FullInstance: 1);
        var outputPath = OutputPath("existing.package");
        await File.WriteAllTextAsync(outputPath, "not a real package");

        await Assert.ThrowsAsync<IOException>(() =>
            _sut.WriteStringTableAsync(outputPath, key, [new StringTableEntry(1, "value")]));

        Assert.Equal("not a real package", await File.ReadAllTextAsync(outputPath));
    }

    [Fact]
    public async Task ReadStringTableAsync_KeyNotPresentInPackage_ThrowsRatherThanReturningEmpty()
    {
        var outputPath = OutputPath("mismatch.package");
        await _sut.WriteStringTableAsync(outputPath, new StblKey(0, 1), [new StringTableEntry(1, "x")]);

        var differentKey = new StblKey(0, 999);

        await Assert.ThrowsAnyAsync<Exception>(() => _sut.ReadStringTableAsync(outputPath, differentKey));
    }

    [Fact]
    public async Task WriteStringTablesAsync_MultipleStbls_AllAppearInOnePackage()
    {
        var keyA = new StblKey(Group: 0, FullInstance: 0x01UL);
        var keyB = new StblKey(Group: 0, FullInstance: 0x02UL);
        var outputPath = OutputPath("multi.package");

        var tables = new Dictionary<StblKey, IReadOnlyList<StringTableEntry>>
        {
            [keyA] = [new StringTableEntry(1, "From table A")],
            [keyB] = [new StringTableEntry(2, "From table B")],
        };

        await _sut.WriteStringTablesAsync(outputPath, tables);
        var found = await _sut.FindStringTablesAsync(outputPath);

        Assert.Equal(2, found.Count);
        Assert.Contains(keyA, found);
        Assert.Contains(keyB, found);
        var entriesA = await _sut.ReadStringTableAsync(outputPath, keyA);
        var entriesB = await _sut.ReadStringTableAsync(outputPath, keyB);
        Assert.Equal("From table A", Assert.Single(entriesA).Value);
        Assert.Equal("From table B", Assert.Single(entriesB).Value);
    }

    [Fact]
    public async Task WriteStringTablesAsync_OutputAlreadyExists_ThrowsAndDoesNotOverwrite()
    {
        var outputPath = OutputPath("multi-existing.package");
        await File.WriteAllTextAsync(outputPath, "not a real package");
        var tables = new Dictionary<StblKey, IReadOnlyList<StringTableEntry>>
        {
            [new StblKey(0, 1)] = [new StringTableEntry(1, "x")],
        };

        await Assert.ThrowsAsync<IOException>(() => _sut.WriteStringTablesAsync(outputPath, tables));
        Assert.Equal("not a real package", await File.ReadAllTextAsync(outputPath));
    }
}
