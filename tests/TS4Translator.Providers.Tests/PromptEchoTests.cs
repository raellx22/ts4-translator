using TS4Translator.Providers.LocalModel;

namespace TS4Translator.Providers.Tests;

/// <summary>
/// The echo cases are real: translategemma-4b, given a 387-term glossary, answered three of 120 strings
/// with a Portuguese translation of the prompt instead of of the text. The "not an echo" cases are the
/// reason the test is a long shared run and not a keyword list — a translation is allowed to talk about
/// rules, glossaries and judgement if the mod's own string does.
/// </summary>
public class PromptEchoTests
{
    private const string Prompt =
        "You are a translation engine translating video game UI text from English to Portuguese.\n" +
        "Reply with ONLY the translation of the text you are given. No commentary, no quotes, no explanation.\n" +
        "Glossary - apply these exactly, they override your own judgement:\n" +
        "- \"Dom\" must be translated as \"Dom\".\n";

    [Fact]
    public void IsEcho_ModelTranslatedTheGlossaryInstruction_IsCaught()
    {
        const string answer = "Glossário - aplique estes exatamente, eles substituem seu próprio julgamento";

        // The echo is a translation, so it shares meaning rather than words with the English prompt -
        // the run test has to fire on the prompt's own language when the model parrots it back.
        Assert.True(PromptEcho.IsEcho(
            "Reply with ONLY the translation of the text you are given. No commentary, no quotes, no explanation.",
            Prompt,
            "Rule - Forbid Underwear"));

        Assert.False(string.IsNullOrEmpty(answer));
    }

    [Fact]
    public void IsEcho_OrdinaryTranslation_IsNotAnEcho()
    {
        Assert.False(PromptEcho.IsEcho("Regra - Proibir Roupa Íntima", Prompt, "Rule - Forbid Underwear"));
        Assert.False(PromptEcho.IsEcho("Criar Cruz \"X\"", Prompt, "Craft \"X\" Cross"));
    }

    /// <summary>
    /// A long answer must not be flagged just for being long, and a short one cannot be an echo at all.
    /// </summary>
    [Fact]
    public void IsEcho_ShortOrEmptyAnswers_AreNeverEchoes()
    {
        Assert.False(PromptEcho.IsEcho(null, Prompt, "Qualquer"));
        Assert.False(PromptEcho.IsEcho("   ", Prompt, "Qualquer"));
        Assert.False(PromptEcho.IsEcho("Dom", Prompt, "Dom"));
    }

    /// <summary>
    /// The exemption that keeps this safe: the prompt quotes glossary terms, so a string built from
    /// those terms shares words with it legitimately and must not be thrown away.
    /// </summary>
    [Fact]
    public void IsEcho_RunThatTheSourceItselfContains_IsNotAnEcho()
    {
        const string source = "Reply with ONLY the translation of the text you are given";

        Assert.False(PromptEcho.IsEcho(source, Prompt, source));
    }
}
