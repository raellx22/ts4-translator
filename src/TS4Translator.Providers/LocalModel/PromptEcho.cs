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

    /// <param name="alsoLegitimate">
    /// Text the answer is <b>supposed</b> to contain, and which therefore proves nothing when it is
    /// shared with the prompt — in practice the required translations of the glossary terms quoted to
    /// the model for this string.
    /// </param>
    /// <remarks>
    /// This parameter is load-bearing now that the glossary travels in the user turn per string rather
    /// than in the shared system prompt. The prompt for one string quotes the Portuguese it must use, so
    /// a long enough required translation would appear verbatim in both the prompt and a perfectly
    /// correct answer — and this check would blank the one answer that got the terminology exactly right.
    /// </remarks>
    /// <summary>Whether <paramref name="answer"/> is the model reciting <paramref name="promptText"/>.</summary>
    public static bool IsEcho(string? answer, string promptText, string sourceText, IReadOnlyList<string>? alsoLegitimate = null)
    {
        ArgumentNullException.ThrowIfNull(promptText);
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

        var promptRuns = Runs(Words(promptText));

        // Runs the source itself contains are not evidence of anything: the prompt quotes glossary terms,
        // and a string made of those terms would legitimately share words with it.
        promptRuns.ExceptWith(Runs(Words(sourceText)));

        if (alsoLegitimate is { Count: > 0 })
        {
            foreach (var legitimate in alsoLegitimate)
            {
                promptRuns.ExceptWith(Runs(Words(legitimate)));
            }
        }

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
