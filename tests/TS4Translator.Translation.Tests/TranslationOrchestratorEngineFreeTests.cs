using System.Globalization;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Memory;
using TS4Translator.Core.Translation;
using TS4Translator.Core.Validation;

namespace TS4Translator.Translation.Tests;

/// <summary>
/// The strings that must never reach an engine, and the accounting that says so.
/// </summary>
public class TranslationOrchestratorEngineFreeTests
{
    private static readonly CultureInfo English = new("en-US");
    private static readonly CultureInfo PortugueseBrazil = new("pt-BR");

    private readonly FakeTranslationMemory _memory = new();

    private TranslationOrchestrator Sut => new(new TokenProtector(), new TranslationValidator(), new BatchBuilder(), _memory);

    private static GlossaryTerm Term(int id, string original, string translation, bool neverTranslate = false) =>
        new(id, original, translation, neverTranslate, CaseSensitive: false, Notes: null, ProjectId: null);

    /// <summary>
    /// StringTranslatability has said "Code never leaves the app" since it was written; the path that
    /// runs the engines consulted it nowhere, so a bulk run sent every tuning identifier to the paid API
    /// or the local GPU. On the import that motivated the classifier that was 128 strings in 1.000.
    /// </summary>
    [Fact]
    public async Task TranslateAsync_TuningIdentifiers_NeverReachTheProvider()
    {
        var provider = new FakeTranslationProvider();
        var items = new[]
        {
            new TranslationSourceItem("1", "debug_savedActor2"),
            new TranslationSourceItem("2", "craftGags1_cd"),
            new TranslationSourceItem("3", "Open the settings"),
        };

        var outcomes = await Sut.TranslateAsync(items, provider, English, PortugueseBrazil);

        Assert.Equal([1], provider.ObservedBatchSizes);
        Assert.Equal("debug_savedActor2", outcomes[0].TranslatedText);
        Assert.Equal("craftGags1_cd", outcomes[1].TranslatedText);
        Assert.Equal("[fake] Open the settings", outcomes[2].TranslatedText);
    }

    /// <summary>An identifier kept as-is is the correct answer, not a row to review.</summary>
    [Fact]
    public async Task TranslateAsync_TuningIdentifier_SucceedsWithAnObservationRatherThanAWarning()
    {
        var provider = new FakeTranslationProvider();

        var outcomes = await Sut.TranslateAsync(
            [new TranslationSourceItem("1", "debug_savedActor2")], provider, English, PortugueseBrazil);

        Assert.True(outcomes[0].Success);
        var issue = Assert.Single(outcomes[0].Issues);
        Assert.Equal(ValidationIssueCode.NotTranslatable, issue.Code);
        Assert.Equal(ValidationSeverity.Info, issue.Severity);
    }

    [Fact]
    public async Task TranslateAsync_GlossaryCoversTheWholeString_IsAnsweredWithoutTheProvider()
    {
        var provider = new FakeTranslationProvider();
        var items = new[] { new TranslationSourceItem("1", "Body Selector") };
        var glossary = new[] { Term(1, "Body Selector", "Seletor de Corpo") };

        var outcomes = await Sut.TranslateAsync(items, provider, English, PortugueseBrazil, glossary);

        Assert.Empty(provider.ObservedBatchSizes);
        Assert.Equal("Seletor de Corpo", outcomes[0].TranslatedText);
        Assert.True(outcomes[0].Success);
        Assert.True(outcomes[0].AppliedFromGlossary);
        Assert.False(outcomes[0].ReusedFromMemory);
    }

    [Fact]
    public async Task TranslateAsync_GlossaryCoversOnlyPartOfTheString_StillCallsTheProvider()
    {
        var provider = new FakeTranslationProvider();
        var items = new[] { new TranslationSourceItem("1", "Open the Body Selector now") };
        var glossary = new[] { Term(1, "Body Selector", "Seletor de Corpo") };

        await Sut.TranslateAsync(items, provider, English, PortugueseBrazil, glossary);

        Assert.Equal([1], provider.ObservedBatchSizes);
    }

    /// <summary>
    /// Memory holds what a human approved for this exact string, so it outranks a generic term rule.
    /// </summary>
    [Fact]
    public async Task TranslateAsync_MemoryAndGlossaryBothAnswer_MemoryWins()
    {
        await _memory.SaveAsync(new TranslationMemoryEntry(
            "Body Selector", English, PortugueseBrazil, "Escolha de Corpo", "manual", null, DateTimeOffset.UtcNow));

        var provider = new FakeTranslationProvider();
        var glossary = new[] { Term(1, "Body Selector", "Seletor de Corpo") };

        var outcomes = await Sut.TranslateAsync(
            [new TranslationSourceItem("1", "Body Selector")], provider, English, PortugueseBrazil, glossary);

        Assert.Equal("Escolha de Corpo", outcomes[0].TranslatedText);
        Assert.True(outcomes[0].ReusedFromMemory);
        Assert.Empty(provider.ObservedBatchSizes);
    }

    /// <summary>
    /// A glossary rule the user wrote outranks a guess about text shape: "WickedWhims" reads as a
    /// camelCase identifier, and it is a name the glossary knows.
    /// </summary>
    [Fact]
    public async Task TranslateAsync_CamelCaseNameCoveredByTheGlossary_IsCreditedToTheGlossary()
    {
        var provider = new FakeTranslationProvider();
        var glossary = new[] { Term(1, "WickedWhims", "WickedWhims", neverTranslate: true) };

        var outcomes = await Sut.TranslateAsync(
            [new TranslationSourceItem("1", "WickedWhims")], provider, English, PortugueseBrazil, glossary);

        Assert.Equal("WickedWhims", outcomes[0].TranslatedText);
        Assert.True(outcomes[0].AppliedFromGlossary);
    }

    /// <summary>
    /// Nothing answered without an engine may enter translation memory as if an engine had produced it.
    /// </summary>
    [Fact]
    public async Task TranslateAsync_AnsweredWithoutAnEngine_IsNotWrittenToMemory()
    {
        var provider = new FakeTranslationProvider();
        var glossary = new[] { Term(1, "Body Selector", "Seletor de Corpo") };

        await Sut.TranslateAsync(
            [new TranslationSourceItem("1", "Body Selector"), new TranslationSourceItem("2", "debug_savedActor2")],
            provider, English, PortugueseBrazil, glossary);

        Assert.Null(await _memory.FindExactAsync("Body Selector", English, PortugueseBrazil));
        Assert.Null(await _memory.FindExactAsync("debug_savedActor2", English, PortugueseBrazil));
    }

    /// <summary>
    /// A revision pass has nothing to improve about an identifier: the correct rendering is the
    /// identifier, so the best an engine could do is leave it alone and the likely outcome is damage.
    /// </summary>
    [Fact]
    public async Task RefineAsync_TuningIdentifier_NeverReachesTheProvider()
    {
        var provider = new FakeTranslationProvider(supportsCustomPrompt: true);
        var items = new[]
        {
            new RefinementSourceItem("1", "debug_savedActor2", "depurar_atorSalvo2"),
            new RefinementSourceItem("2", "Open the settings", "Abra as configuracoes"),
        };

        var outcomes = await Sut.RefineAsync(items, provider, English, PortugueseBrazil);

        Assert.Equal([1], provider.ObservedBatchSizes);
        Assert.Equal("debug_savedActor2", outcomes[0].TranslatedText);
        Assert.True(outcomes[0].Success);
    }

    /// <summary>
    /// Unlike a first translation, a revision must still reach the engine even when a term covers the
    /// whole string: the user picked an engine and asked it to improve that line.
    /// </summary>
    [Fact]
    public async Task RefineAsync_GlossaryCoversTheWholeString_StillCallsTheProvider()
    {
        var provider = new FakeTranslationProvider();
        var glossary = new[] { Term(1, "Body Selector", "Seletor de Corpo") };

        await Sut.RefineAsync(
            [new RefinementSourceItem("1", "Body Selector", "Escolhedor")], provider, English, PortugueseBrazil, glossary);

        Assert.Equal([1], provider.ObservedBatchSizes);
    }

    [Fact]
    public async Task RefineAsync_EveryItemIsAnIdentifier_CallsNoProviderAtAll()
    {
        var provider = new FakeTranslationProvider();

        var outcomes = await Sut.RefineAsync(
            [new RefinementSourceItem("1", "debug_savedActor2", "algo")], provider, English, PortugueseBrazil);

        Assert.Empty(provider.ObservedBatchSizes);
        Assert.Single(outcomes);
    }

    [Fact]
    public async Task TranslateAsync_EveryStringAnsweredLocally_CallsNoProviderAtAll()
    {
        var provider = new FakeTranslationProvider();
        var glossary = new[] { Term(1, "Body Selector", "Seletor de Corpo") };

        var outcomes = await Sut.TranslateAsync(
            [new TranslationSourceItem("1", "Body Selector"), new TranslationSourceItem("2", "var2")],
            provider, English, PortugueseBrazil, glossary);

        Assert.Empty(provider.ObservedBatchSizes);
        Assert.Equal(2, outcomes.Count);
        Assert.All(outcomes, outcome => Assert.True(outcome.Success));
    }
}
