namespace TS4Translator.Providers.LocalModel;

/// <summary>
/// Catches the answer where the model translated the instructions instead of the text.
/// </summary>
/// <remarks>
/// Measured, and it only shows up once the glossary is large. Running translategemma-4b over 120 real
/// strings with 387 glossary terms in the prompt, three answers came back as Portuguese renderings of
/// the system prompt itself — <c>Rule - Forbid Underwear</c> produced <i>"Glossário - aplique estes
/// exatamente, eles substituem seu próprio julgamento"</i>. Hy-MT2 did it zero times on the same run, so
/// this is a per-model habit that appears under prompt pressure rather than a universal one.
///
/// It is the worst possible failure to leave unhandled: the text is fluent Portuguese, keeps no markers
/// to break, is a plausible length, and passes every check the app has — it would ship into the game's
/// string table as a sentence about a glossary. Blanking it is deliberate: the validator then reports an
/// empty translation and the string goes to review, which is the honest outcome for an answer that never
/// contained a translation at all.
///
/// The test is a shared run of words with the prompt that the source does not explain. Long enough not
/// to fire on ordinary language, and anchored on the prompt the app actually sent rather than on a list
/// of phrases to keep in sync with it.
/// </remarks>
public static class PromptEcho
{
    /// <summary>
    /// How many consecutive words shared with the prompt make an answer an echo rather than a coincidence.
    /// </summary>
    /// <remarks>
    /// Six. The prompt is English and the answer is Portuguese, so an honest overlap of six running words
    /// essentially cannot happen; the observed echoes shared entire clauses.
    /// </remarks>
    private const int RunLength = 6;

    /// <summary>Whether <paramref name="answer"/> is the model reciting <paramref name="systemPrompt"/>.</summary>
    public static bool IsEcho(string? answer, string systemPrompt, string sourceText)
    {
        ArgumentNullException.ThrowIfNull(systemPrompt);
        ArgumentNullException.ThrowIfNull(sourceText);

        if (string.IsNullOrWhiteSpace(answer))
        {
            return false;
        }

        var answerWords = Words(answer);
        if (answerWords.Length < RunLength)
        {
            return false;
        }

        var promptRuns = Runs(Words(systemPrompt));

        // Runs the source itself contains are not evidence of anything: the prompt quotes glossary terms,
        // and a string made of those terms would legitimately share words with it.
        promptRuns.ExceptWith(Runs(Words(sourceText)));

        return Runs(answerWords).Overlaps(promptRuns);
    }

    private static HashSet<string> Runs(string[] words)
    {
        var runs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index + RunLength <= words.Length; index++)
        {
            runs.Add(string.Join(' ', words.AsSpan(index, RunLength).ToArray()));
        }

        return runs;
    }

    private static string[] Words(string text) =>
        text.Split([' ', '\n', '\r', '\t', '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '-'],
            StringSplitOptions.RemoveEmptyEntries);
}
