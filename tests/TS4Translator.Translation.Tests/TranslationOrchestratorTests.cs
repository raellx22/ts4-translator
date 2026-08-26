using System.Globalization;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Translation;
using TS4Translator.Core.Validation;

namespace TS4Translator.Translation.Tests;

public class TranslationOrchestratorTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    private readonly FakeTranslationMemory _memory = new();
    private readonly TranslationOrchestrator _sut;

    public TranslationOrchestratorTests()
    {
        _sut = new TranslationOrchestrator(new TokenProtector(), new TranslationValidator(), new BatchBuilder(), _memory);
    }

    [Fact]
    public async Task TranslateAsync_NewItem_CallsProviderAndSavesToMemory()
    {
        var provider = new FakeTranslationProvider();
        var items = new[] { new TranslationSourceItem("1", "Hello there.") };

        var outcomes = await _sut.TranslateAsync(items, provider, English, PortugueseBrazil);

        var outcome = Assert.Single(outcomes);
        Assert.True(outcome.Success);
        Assert.Equal("[fake] Hello there.", outcome.TranslatedText);
        Assert.Equal(1, _memory.SaveCallCount);
    }

    [Fact]
    public async Task TranslateAsync_ItemAlreadyInMemory_SkipsProviderEntirely()
    {
        await _memory.SaveAsync(new Core.Memory.TranslationMemoryEntry("Hello there.", English, PortugueseBrazil, "Olá.", "manual", null, DateTimeOffset.UtcNow));
        var provider = new FakeTranslationProvider();
        var items = new[] { new TranslationSourceItem("1", "Hello there.") };

        var outcomes = await _sut.TranslateAsync(items, provider, English, PortugueseBrazil);

        Assert.Equal("Olá.", outcomes[0].TranslatedText);
        Assert.Empty(provider.ObservedBatchSizes);
    }

    [Fact]
    public async Task TranslateAsync_PreservesTokensThroughProtectAndRestore()
    {
        var provider = new FakeTranslationProvider(text => text.Replace("Ask", "Pedir a"));
        var items = new[] { new TranslationSourceItem("1", "Ask {0.SimFirstName} to stay.") };

        var outcomes = await _sut.TranslateAsync(items, provider, English, PortugueseBrazil);

        Assert.True(outcomes[0].Success);
        Assert.Contains("{0.SimFirstName}", outcomes[0].TranslatedText);
    }

    [Fact]
    public async Task TranslateAsync_ProviderDropsAResult_ReturnsFailureWithProviderResultMissing()
    {
        var provider = new FakeTranslationProvider(misbehavior: FakeProviderMisbehavior.DropFirstResult);
        var items = new[] { new TranslationSourceItem("1", "Only item.") };

        var outcomes = await _sut.TranslateAsync(items, provider, English, PortugueseBrazil);

        Assert.False(outcomes[0].Success);
        Assert.Null(outcomes[0].TranslatedText);
        Assert.Contains(outcomes[0].Issues, i => i.Code == ValidationIssueCode.ProviderResultMissing);
        Assert.Equal(0, _memory.SaveCallCount);
    }

    [Fact]
    public async Task TranslateAsync_ProviderDuplicatesAResult_ReturnsFailureWithProviderResultDuplicated()
    {
        var provider = new FakeTranslationProvider(misbehavior: FakeProviderMisbehavior.DuplicateFirstResult);
        var items = new[] { new TranslationSourceItem("1", "Only item.") };

        var outcomes = await _sut.TranslateAsync(items, provider, English, PortugueseBrazil);

        Assert.False(outcomes[0].Success);
        Assert.Contains(outcomes[0].Issues, i => i.Code == ValidationIssueCode.ProviderResultDuplicated);
    }

    [Fact]
    public async Task TranslateAsync_ProviderCorruptsAToken_ReturnsFailureButKeepsBestEffortText()
    {
        var provider = new FakeTranslationProvider(misbehavior: FakeProviderMisbehavior.StripTokensFromFirstResult);
        var items = new[] { new TranslationSourceItem("1", "Ask {0.SimFirstName} to stay.") };

        var outcomes = await _sut.TranslateAsync(items, provider, English, PortugueseBrazil);

        Assert.False(outcomes[0].Success);
        Assert.NotNull(outcomes[0].TranslatedText);
        Assert.Contains(outcomes[0].Issues, i => i.Code == ValidationIssueCode.MissingToken);
        Assert.Equal(0, _memory.SaveCallCount);
    }

    [Fact]
    public async Task TranslateAsync_MoreItemsThanBatchSize_SplitsAcrossMultipleProviderCalls()
    {
        var orchestrator = new TranslationOrchestrator(new TokenProtector(), new TranslationValidator(), new BatchBuilder(maxItemsPerBatch: 2), _memory);
        var provider = new FakeTranslationProvider();
        var items = Enumerable.Range(1, 5).Select(i => new TranslationSourceItem(i.ToString(CultureInfo.InvariantCulture), $"Item {i}")).ToList();

        var outcomes = await orchestrator.TranslateAsync(items, provider, English, PortugueseBrazil);

        Assert.Equal(5, outcomes.Count);
        Assert.All(outcomes, o => Assert.True(o.Success));
        Assert.Equal(3, provider.ObservedBatchSizes.Count);
        Assert.Equal([2, 2, 1], provider.ObservedBatchSizes);
    }

    [Fact]
    public async Task TranslateAsync_MixOfMemoryHitsAndNewItems_ReturnsResultsInOriginalOrder()
    {
        await _memory.SaveAsync(new Core.Memory.TranslationMemoryEntry("Second.", English, PortugueseBrazil, "Segundo.", "manual", null, DateTimeOffset.UtcNow));
        var provider = new FakeTranslationProvider();
        var items = new[]
        {
            new TranslationSourceItem("1", "First."),
            new TranslationSourceItem("2", "Second."),
            new TranslationSourceItem("3", "Third."),
        };

        var outcomes = await _sut.TranslateAsync(items, provider, English, PortugueseBrazil);

        Assert.Equal(["1", "2", "3"], outcomes.Select(o => o.Id));
        Assert.Equal("[fake] First.", outcomes[0].TranslatedText);
        Assert.Equal("Segundo.", outcomes[1].TranslatedText);
        Assert.Equal("[fake] Third.", outcomes[2].TranslatedText);
    }

    [Fact]
    public async Task TranslateAsync_GlossaryTermAppearsInBatch_IsForwardedToProvider()
    {
        var provider = new FakeTranslationProvider();
        var items = new[] { new TranslationSourceItem("1", "Talk to the Grim Reaper.") };
        var glossary = new[] { new GlossaryTerm(1, "Grim Reaper", "Ceifador", NeverTranslate: false, CaseSensitive: false, Notes: null, ProjectId: null) };

        await _sut.TranslateAsync(items, provider, English, PortugueseBrazil, glossary);

        var observed = Assert.Single(provider.ObservedGlossaryTerms);
        var term = Assert.Single(observed!);
        Assert.Equal("Grim Reaper", term.Original);
    }

    [Fact]
    public async Task TranslateAsync_GlossaryTermDoesNotAppearInBatch_IsNotForwardedToProvider()
    {
        var provider = new FakeTranslationProvider();
        var items = new[] { new TranslationSourceItem("1", "Hello there.") };
        var glossary = new[] { new GlossaryTerm(1, "Grim Reaper", "Ceifador", NeverTranslate: false, CaseSensitive: false, Notes: null, ProjectId: null) };

        await _sut.TranslateAsync(items, provider, English, PortugueseBrazil, glossary);

        var observed = Assert.Single(provider.ObservedGlossaryTerms);
        Assert.Empty(observed!);
    }

    [Fact]
    public async Task TranslateAsync_GlossaryFilteringIsPerBatch_OnlyRelevantTermsGoToEachBatch()
    {
        var orchestrator = new TranslationOrchestrator(new TokenProtector(), new TranslationValidator(), new BatchBuilder(maxItemsPerBatch: 1), _memory);
        var provider = new FakeTranslationProvider();
        var items = new[]
        {
            new TranslationSourceItem("1", "Talk to the Grim Reaper."),
            new TranslationSourceItem("2", "Hello there."),
        };
        var glossary = new[] { new GlossaryTerm(1, "Grim Reaper", "Ceifador", NeverTranslate: false, CaseSensitive: false, Notes: null, ProjectId: null) };

        await orchestrator.TranslateAsync(items, provider, English, PortugueseBrazil, glossary);

        Assert.Equal(2, provider.ObservedGlossaryTerms.Count);
        Assert.NotEmpty(provider.ObservedGlossaryTerms[0]!);
        Assert.Empty(provider.ObservedGlossaryTerms[1]!);
    }

    [Fact]
    public async Task TranslateAsync_NoGlossaryProvided_PassesNullToProvider()
    {
        var provider = new FakeTranslationProvider();
        var items = new[] { new TranslationSourceItem("1", "Hello there.") };

        await _sut.TranslateAsync(items, provider, English, PortugueseBrazil);

        var observed = Assert.Single(provider.ObservedGlossaryTerms);
        Assert.Null(observed);
    }
}
