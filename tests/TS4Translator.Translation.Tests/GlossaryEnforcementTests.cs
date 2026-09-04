using System.Globalization;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Translation;
using TS4Translator.Core.Validation;

namespace TS4Translator.Translation.Tests;

/// <summary>
/// Terminology applied by substitution rather than by asking the engine to comply — the only form of it
/// that reaches an engine with no prompt input at all.
/// </summary>
public class GlossaryEnforcementTests
{
    private static readonly CultureInfo English = new("en-US");
    private static readonly CultureInfo PortugueseBrazil = new("pt-BR");

    private readonly FakeTranslationMemory _memory = new();

    private TranslationOrchestrator Sut => new(new TokenProtector(), new TranslationValidator(), new BatchBuilder(), _memory);

    private static GlossaryTerm Term(int id, string original, string translation, bool neverTranslate = false, bool caseSensitive = false) =>
        new(id, original, translation, neverTranslate, caseSensitive, Notes: null, ProjectId: null);

    [Fact]
    public void Protect_NeverTranslateTerm_IsTakenOutOfTheTextTheEngineSees()
    {
        var enforcement = GlossaryEnforcement.For(
            GlossaryIndex.For([Term(1, "WickedWhims", "WickedWhims", neverTranslate: true, caseSensitive: true)]),
            providerHonoursPrompt: true);

        var result = new TokenProtector().Protect("Open the WickedWhims settings", enforcement);

        Assert.DoesNotContain("WickedWhims", result.ProtectedText, StringComparison.Ordinal);
        var token = Assert.Single(result.Tokens);
        Assert.True(token.IsGlossaryTerm);
        Assert.Equal("WickedWhims", token.ValueToRestore);
    }

    [Fact]
    public void Restore_NeverTranslateTerm_ComesBackVerbatim()
    {
        var protector = new TokenProtector();
        var enforcement = GlossaryEnforcement.For(
            GlossaryIndex.For([Term(1, "WickedWhims", "WickedWhims", neverTranslate: true, caseSensitive: true)]),
            providerHonoursPrompt: true);

        var protectedString = protector.Protect("Open the WickedWhims settings", enforcement);
        var placeholder = protectedString.Tokens[0].Placeholder;

        var restored = protector.Restore($"Abra as configurações do {placeholder}", protectedString);

        Assert.Equal("Abra as configurações do WickedWhims", restored.RestoredText);
        Assert.True(restored.IsValid);
    }

    /// <summary>
    /// The rule that makes the cheapest lane obey terminology at all: an engine with no prompt gets the
    /// required Portuguese substituted in, because the alternative there is no terminology whatsoever.
    /// </summary>
    [Fact]
    public void Restore_ProviderWithoutPromptSupport_RequiredTranslationIsSubstitutedIn()
    {
        var protector = new TokenProtector();
        var enforcement = GlossaryEnforcement.For(
            GlossaryIndex.For([Term(1, "Body Selector", "Seletor de Corpo")]),
            providerHonoursPrompt: false);

        var protectedString = protector.Protect("Open the Body Selector now", enforcement);
        Assert.DoesNotContain("Body Selector", protectedString.ProtectedText, StringComparison.Ordinal);

        var placeholder = protectedString.Tokens[0].Placeholder;
        var restored = protector.Restore($"Abra o {placeholder} agora", protectedString);

        Assert.Equal("Abra o Seletor de Corpo agora", restored.RestoredText);
    }

    /// <summary>
    /// Where a prompt exists it is the better tool: freezing a noun phrase mid-sentence costs Portuguese
    /// its agreement, and the post-hoc check catches what the prompt misses.
    /// </summary>
    [Fact]
    public void Protect_ProviderWithPromptSupport_RequiredTranslationIsLeftToThePrompt()
    {
        var enforcement = GlossaryEnforcement.For(
            GlossaryIndex.For([Term(1, "Body Selector", "Seletor de Corpo")]),
            providerHonoursPrompt: true);

        var result = new TokenProtector().Protect("Open the Body Selector now", enforcement);

        Assert.Contains("Body Selector", result.ProtectedText, StringComparison.Ordinal);
        Assert.Empty(result.Tokens);
    }

    [Fact]
    public void Protect_OverlappingTerms_TheLongerOneClaimsTheSpan()
    {
        var enforcement = GlossaryEnforcement.For(
            GlossaryIndex.For([
                Term(1, "Sex", "Sexo", neverTranslate: true),
                Term(2, "Sex Autonomy", "Autonomia Sexual", neverTranslate: true),
            ]),
            providerHonoursPrompt: true);

        var result = new TokenProtector().Protect("Open Sex Autonomy here", enforcement);

        var token = Assert.Single(result.Tokens);
        Assert.Equal("Sex Autonomy", token.ValueToRestore);
    }

    /// <summary>A term must not fire inside a longer word, exactly as in the prompt path.</summary>
    [Fact]
    public void Protect_TermInsideALongerWord_IsNotSubstituted()
    {
        var enforcement = GlossaryEnforcement.For(
            GlossaryIndex.For([Term(1, "Male", "Masculino", neverTranslate: true)]),
            providerHonoursPrompt: true);

        var result = new TokenProtector().Protect("Female characters only", enforcement);

        Assert.Empty(result.Tokens);
    }

    /// <summary>
    /// A string that is nothing but the term would go to the engine as a lone marker to echo — the prompt
    /// shape that made a 7B model answer with the bare stem. The orchestrator answers it from the
    /// glossary long before this, but the protector must not create that shape either way.
    /// </summary>
    [Fact]
    public void Protect_TermCoversTheWholeString_IsNotSubstituted()
    {
        var enforcement = GlossaryEnforcement.For(
            GlossaryIndex.For([Term(1, "WickedWhims", "WickedWhims", neverTranslate: true)]),
            providerHonoursPrompt: true);

        var result = new TokenProtector().Protect("WickedWhims", enforcement);

        Assert.Equal("WickedWhims", result.ProtectedText);
        Assert.Empty(result.Tokens);
    }

    /// <summary>
    /// Game syntax and glossary terms share one numbering, and the order check must read the string
    /// rather than the order the tokens happened to be recorded in.
    /// </summary>
    [Fact]
    public void Restore_GlossaryTokenBetweenGameTokens_IsNotReportedAsReordered()
    {
        var protector = new TokenProtector();
        var enforcement = GlossaryEnforcement.For(
            GlossaryIndex.For([Term(1, "WickedWhims", "WickedWhims", neverTranslate: true)]),
            providerHonoursPrompt: true);

        var protectedString = protector.Protect("{0.SimFirstName} opened WickedWhims\nagain", enforcement);
        var inOrder = string.Join(" ", protectedString.Tokens.Select(token => token.Placeholder));

        var restored = protector.Restore(inOrder, protectedString);

        Assert.DoesNotContain(restored.Issues, issue => issue.Kind == Core.Tokens.TokenValidationIssueKind.ReorderedToken);
    }

    /// <summary>
    /// End to end: a provider that declares no glossary support gets no glossary argument, and the
    /// terminology still lands — which is the whole point of the substitution path.
    /// </summary>
    [Fact]
    public async Task TranslateAsync_ProviderWithoutGlossarySupport_GetsNoTermsButStillHonoursThem()
    {
        var provider = new FakeTranslationProvider(supportsGlossary: false);
        var items = new[] { new TranslationSourceItem("1", "Open the Body Selector now") };
        var glossary = new[] { Term(1, "Body Selector", "Seletor de Corpo") };

        var outcomes = await Sut.TranslateAsync(items, provider, English, PortugueseBrazil, glossary);

        Assert.Null(Assert.Single(provider.ObservedGlossaryTerms));
        Assert.Contains("Seletor de Corpo", outcomes[0].TranslatedText!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The hole this closes: every engine the app drives itself could ignore the whole glossary and the
    /// run reported success.
    /// </summary>
    [Fact]
    public async Task TranslateAsync_EngineIgnoresAGlossaryTerm_IsFlaggedForReview()
    {
        var provider = new FakeTranslationProvider(_ => "Abra o Escolhedor de Corpo agora");
        var items = new[] { new TranslationSourceItem("1", "Open the Body Selector now") };
        var glossary = new[] { Term(1, "Body Selector", "Seletor de Corpo") };

        var outcomes = await Sut.TranslateAsync(items, provider, English, PortugueseBrazil, glossary);

        Assert.False(outcomes[0].Success);
        Assert.Contains(outcomes[0].Issues, issue => issue.Code == ValidationIssueCode.GlossaryNotRespected);
    }

    [Fact]
    public async Task TranslateAsync_EngineHonoursTheTerm_IsNotFlagged()
    {
        var provider = new FakeTranslationProvider(_ => "Abra o Seletor de Corpo agora");
        var items = new[] { new TranslationSourceItem("1", "Open the Body Selector now") };
        var glossary = new[] { Term(1, "Body Selector", "Seletor de Corpo") };

        var outcomes = await Sut.TranslateAsync(items, provider, English, PortugueseBrazil, glossary);

        Assert.True(outcomes[0].Success);
    }
}
