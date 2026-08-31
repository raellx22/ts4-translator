using System.Globalization;
using TS4Translator.Core.Memory;
using TS4Translator.Core.Translation;

namespace TS4Translator.Translation.Tests;

public sealed class TranslationOrchestratorRefineTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    private static (TranslationOrchestrator Orchestrator, FakeTranslationMemory Memory) CreateOrchestrator()
    {
        var memory = new FakeTranslationMemory();
        var orchestrator = new TranslationOrchestrator(
            new TokenProtector(), new TranslationValidator(), new BatchBuilder(), memory);
        return (orchestrator, memory);
    }

    /// <summary>
    /// The whole reason RefineAsync is a separate method: every item being refined is already in
    /// translation memory, so going through TranslateAsync would return the very draft the user is
    /// trying to improve and never call the provider at all.
    /// </summary>
    [Fact]
    public async Task RefineAsync_ItemAlreadyInTranslationMemory_StillCallsTheProvider()
    {
        var (orchestrator, memory) = CreateOrchestrator();
        await memory.SaveAsync(new TranslationMemoryEntry(
            "Shallow", English, PortugueseBrazil, "Rasgo", "argos", null, DateTimeOffset.UtcNow));

        var provider = new FakeTranslationProvider(_ => "Raso");

        var outcomes = await orchestrator.RefineAsync(
            [new RefinementSourceItem("1", "Shallow", "Rasgo")], provider, English, PortugueseBrazil);

        Assert.Equal("Raso", Assert.Single(outcomes).TranslatedText);
    }

    [Fact]
    public async Task RefineAsync_ProviderAcceptsAPrompt_SendsTheCurrentDraftForEachId()
    {
        var (orchestrator, _) = CreateOrchestrator();
        var provider = new FakeTranslationProvider(text => $"melhor: {text}", supportsCustomPrompt: true);

        await orchestrator.RefineAsync(
            [
                new RefinementSourceItem("1", "Shallow", "Rasgo"),
                new RefinementSourceItem("2", "Weirdos Hangout", "Engate de Esquisitos"),
            ],
            provider,
            English,
            PortugueseBrazil);

        var instructions = Assert.Single(provider.ObservedCustomInstructions);
        Assert.Contains("REVISION pass", instructions, StringComparison.Ordinal);
        Assert.Contains("1: Rasgo", instructions, StringComparison.Ordinal);
        Assert.Contains("2: Engate de Esquisitos", instructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefineAsync_ProviderCannotTakeAPrompt_DoesNotLeakDraftsIntoItsInstructions()
    {
        var (orchestrator, _) = CreateOrchestrator();
        var provider = new FakeTranslationProvider(_ => "Raso", supportsCustomPrompt: false);

        await orchestrator.RefineAsync(
            [new RefinementSourceItem("1", "Shallow", "Rasgo")],
            provider,
            English,
            PortugueseBrazil,
            customInstructions: "Tom informal.");

        // Argos and Google Translate ignore instructions entirely; sending a prompt-shaped blob would
        // be dead weight at best. They simply retranslate the source with a different engine.
        Assert.Equal("Tom informal.", Assert.Single(provider.ObservedCustomInstructions));
    }

    [Fact]
    public async Task RefineAsync_PlaceholderDroppedByTheRefiner_IsReportedAsNeedingReview()
    {
        var (orchestrator, _) = CreateOrchestrator();
        var provider = new FakeTranslationProvider(
            _ => "sem placeholder nenhum", misbehavior: FakeProviderMisbehavior.StripTokensFromFirstResult);

        var outcomes = await orchestrator.RefineAsync(
            [new RefinementSourceItem("1", "Hi {0.SimFirstName}!", "Oi {0.SimFirstName}!")],
            provider,
            English,
            PortugueseBrazil);

        Assert.False(Assert.Single(outcomes).Success);
    }

    [Fact]
    public async Task RefineAsync_SuccessfulRefinement_ReplacesTheDraftInTranslationMemory()
    {
        var (orchestrator, memory) = CreateOrchestrator();
        await memory.SaveAsync(new TranslationMemoryEntry(
            "Shallow", English, PortugueseBrazil, "Rasgo", "argos", null, DateTimeOffset.UtcNow));

        var provider = new FakeTranslationProvider(_ => "Raso");
        await orchestrator.RefineAsync(
            [new RefinementSourceItem("1", "Shallow", "Rasgo")], provider, English, PortugueseBrazil);

        var stored = await memory.FindExactAsync("Shallow", English, PortugueseBrazil);
        Assert.Equal("Raso", stored!.TranslatedText);
    }

    [Fact]
    public async Task RefineAsync_NoItems_DoesNotCallTheProvider()
    {
        var (orchestrator, _) = CreateOrchestrator();
        var provider = new FakeTranslationProvider();

        var outcomes = await orchestrator.RefineAsync([], provider, English, PortugueseBrazil);

        Assert.Empty(outcomes);
        Assert.Empty(provider.ObservedBatchSizes);
    }
}
