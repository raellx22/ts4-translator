using TS4Translator.Core.Glossary;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.LocalModel;

namespace TS4Translator.Providers.Tests;

/// <summary>
/// The local engine translates one string at a time, so the terminology it is shown has to be that
/// string's — not the whole batch's.
/// </summary>
public class LocalItemPromptTests
{
    private static GlossaryTerm Term(int id, string original, string translation, bool neverTranslate = false) =>
        new(id, original, translation, neverTranslate, CaseSensitive: false, Notes: null, ProjectId: null);

    private static TranslationRequestItem Item(string text, string? draft = null) => new("1", text, draft);

    [Fact]
    public void Build_NoGlossary_IsJustTheText()
    {
        Assert.Equal("Open the settings", LocalItemPrompt.Build(Item("Open the settings"), null));
        Assert.Equal("Open the settings", LocalItemPrompt.Build(Item("Open the settings"), GlossaryIndex.Empty));
    }

    /// <summary>
    /// The change itself: a batch's terms reach the engine, but only the ones about this string. Before,
    /// all 44-to-80 of a batch's terms sat in the system prompt of every single string.
    /// </summary>
    [Fact]
    public void Build_OnlyCarriesTheTermsThatApplyToThisString()
    {
        var glossary = GlossaryIndex.For([
            Term(1, "Body Selector", "Seletor de Corpo"),
            Term(2, "Grim Reaper", "Ceifador"),
            Term(3, "Plug", "Plugue"),
        ]);

        var prompt = LocalItemPrompt.Build(Item("Open the Body Selector"), glossary);

        Assert.Contains("Body Selector", prompt, StringComparison.Ordinal);
        Assert.Contains("Seletor de Corpo", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Ceifador", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Plugue", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TermsBlock_IsIntroducedByTheHeadingTheSystemPromptNames()
    {
        var prompt = LocalItemPrompt.Build(Item("Open the Body Selector"), GlossaryIndex.For([Term(1, "Body Selector", "Seletor de Corpo")]));

        Assert.Contains(LocalItemPrompt.TermsHeading, prompt, StringComparison.Ordinal);
        Assert.StartsWith("Open the Body Selector", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_NoTermApplies_WritesNoTermsBlock()
    {
        var prompt = LocalItemPrompt.Build(Item("Nothing relevant here"), GlossaryIndex.For([Term(1, "Body Selector", "Seletor de Corpo")]));

        Assert.DoesNotContain(LocalItemPrompt.TermsHeading, prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_NeverTranslateTerm_IsWordedAsCopying()
    {
        var prompt = LocalItemPrompt.Build(
            Item("Open WickedWhims"), GlossaryIndex.For([Term(1, "WickedWhims", "WickedWhims", neverTranslate: true)]));

        Assert.Contains("keep \"WickedWhims\" exactly as-is", prompt, StringComparison.Ordinal);
    }

    /// <summary>One string may not reintroduce, for itself, the wall of rules this removed for everyone.</summary>
    [Fact]
    public void Build_ManyTermsMatch_IsCappedAtTheLimit()
    {
        var glossary = GlossaryIndex.For([
            Term(1, "Alpha", "Alfa"), Term(2, "Bravo", "Bravo"), Term(3, "Charlie", "Charlie"),
            Term(4, "Delta", "Delta"), Term(5, "Echo", "Eco"), Term(6, "Foxtrot", "Foxtrote"),
        ]);

        var terms = LocalItemPrompt.TermsFor(Item("Alpha Bravo Charlie Delta Echo Foxtrot"), glossary);

        Assert.Equal(LocalItemPrompt.MaximumTermsPerItem, terms.Count);
    }

    [Fact]
    public void Build_RevisionPass_StillCarriesTheDraft()
    {
        var prompt = LocalItemPrompt.Build(Item("Open the settings", "Abra as configuracoes"), GlossaryIndex.Empty);

        Assert.Contains("Existing translation to improve", prompt, StringComparison.Ordinal);
        Assert.EndsWith("Abra as configuracoes", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TermsAndDraft_KeepsTheSourceFirstAndTheDraftLast()
    {
        var prompt = LocalItemPrompt.Build(
            Item("Open the Body Selector", "Abra o Escolhedor"), GlossaryIndex.For([Term(1, "Body Selector", "Seletor de Corpo")]));

        var terms = prompt.IndexOf(LocalItemPrompt.TermsHeading, StringComparison.Ordinal);
        var draft = prompt.IndexOf("Existing translation", StringComparison.Ordinal);

        Assert.True(terms > 0 && draft > terms);
    }

    /// <summary>
    /// What PromptEcho must be told to forgive: the Portuguese the prompt demands is the Portuguese a
    /// correct answer contains.
    /// </summary>
    [Fact]
    public void RequiredTextsFor_ReturnsWhatACorrectAnswerWouldContain()
    {
        var glossary = GlossaryIndex.For([
            Term(1, "Body Selector", "Seletor de Corpo"),
            Term(2, "WickedWhims", "WickedWhims", neverTranslate: true),
        ]);

        var required = LocalItemPrompt.RequiredTextsFor(Item("Open the Body Selector in WickedWhims"), glossary);

        Assert.Contains("Seletor de Corpo", required);
        Assert.Contains("WickedWhims", required);
    }

    /// <summary>
    /// The regression this guards: a long required translation appears verbatim in both the prompt and a
    /// perfectly correct answer, and the echo check would otherwise blank the one answer that got the
    /// terminology exactly right.
    /// </summary>
    [Fact]
    public void PromptEcho_AnswerUsingALongRequiredTranslation_IsNotTreatedAsAnEcho()
    {
        const string source = "Open the Body Selector Configuration Panel";
        const string required = "Abrir o Painel de Configuracao do Seletor de Corpo";

        var glossary = GlossaryIndex.For([Term(1, "Body Selector Configuration Panel", required)]);
        var item = Item(source);
        var userTurn = LocalItemPrompt.Build(item, glossary);

        var isEcho = PromptEcho.IsEcho(required, "system prompt text\n" + userTurn, source,
            LocalItemPrompt.RequiredTextsFor(item, glossary));

        Assert.False(isEcho);
    }

    /// <summary>And the check it must still perform: reciting the rules is not a translation.</summary>
    [Fact]
    public void PromptEcho_AnswerRecitingTheTermsBlock_IsStillCaught()
    {
        const string prompt = "Glossary - these are substitutions, not suggestions. They override your own judgement";

        Assert.True(PromptEcho.IsEcho(
            "Glossary - these are substitutions, not suggestions. They override your own judgement",
            prompt,
            "Ghost Unemotional",
            ["Seletor de Corpo"]));
    }
}
