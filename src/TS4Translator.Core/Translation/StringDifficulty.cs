using System.Text.RegularExpressions;

namespace TS4Translator.Core.Translation;

/// <summary>
/// Scores how much judgement a string is likely to need, 0 (trivial) to 100 (hardest). Used to split
/// a bulk translation between a cheap engine and an expensive one.
/// </summary>
/// <remarks>
/// Deliberately a transparent heuristic over text shape, not a model: the user has to be able to look
/// at the threshold slider and predict what it will do, and it has to cost nothing to run over tens of
/// thousands of strings before any translation starts.
///
/// The factors are the ones that actually broke literal machine translation on the real WickedWhims
/// corpus. A short UI label ("Shallow", "Average Hips") has none of them and scores near zero. A
/// multi-sentence line carrying placeholders and gendered variants has all of them — and is exactly
/// where Argos dropped the sentence's subject or mangled a token's position.
///
/// What it deliberately does <b>not</b> measure is semantic ambiguity. "Shallow" scores 1 and Argos
/// still rendered it "Rasgo" (a tear) instead of "Raso"; no shape-based score can see that. Catching
/// that class of error is the job of the review sweep and per-row refinement, not of routing.
/// </remarks>
public static partial class StringDifficulty
{
    /// <summary>Sims 4 placeholder tokens, e.g. <c>{0.SimFirstName}</c>, and the app's protected form.</summary>
    [GeneratedRegex(@"\{[^{}]*\}|__TS4_TOKEN_\d+__", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern { get; }

    /// <summary>Markup the game renders, e.g. <c>&lt;font color='#AB0C10'&gt;</c>.</summary>
    [GeneratedRegex(@"<[^<>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex MarkupPattern { get; }

    /// <summary>Gendered/plural variant syntax, e.g. <c>{M0.he}{F0.she}</c> — meaning depends on grammar the engine has to get right.</summary>
    [GeneratedRegex(@"\{[MF]\d+\.", RegexOptions.CultureInvariant)]
    private static partial Regex GenderedVariantPattern { get; }

    [GeneratedRegex(@"[.!?]+(\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceEndPattern { get; }

    /// <summary>Scores <paramref name="text"/> from 0 to 100, optionally folding in what the app knows about it.</summary>
    public static int Score(string? text, StringRiskContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        // Length, capped: a long sentence gives an engine more room to lose the thread, but past a
        // point it stops being a useful signal on its own.
        var score = Math.Min(40, text.Length / 4);

        // Each placeholder is a thing that has to survive translation and land in a position that is
        // grammatical in the target language - the single most common way a translation breaks here.
        score += Math.Min(20, PlaceholderPattern.Count(text) * 10);

        score += Math.Min(15, MarkupPattern.Count(text) * 5);

        // More than one sentence means the engine has to keep context across a boundary.
        var sentences = SentenceEndPattern.Count(text);
        score += Math.Min(15, Math.Max(0, sentences - 1) * 5);

        // Gendered variants need real grammatical agreement in Portuguese, which literal engines
        // routinely get wrong.
        if (GenderedVariantPattern.IsMatch(text))
        {
            score += 10;
        }

        // A bare label with no sentence around it is where word-sense mistakes happen: "Shallow" has no
        // context to disambiguate "raso" from "rasgo". A nudge, not a jump - short labels are the bulk
        // of a mod and must not all be pushed onto the expensive engine by this alone.
        if (IsShortAndContextless(text))
        {
            score += 12;
        }

        if (context is not null)
        {
            // The strongest signal available, and the only one that is evidence rather than a guess:
            // this exact string already came back with a problem.
            if (context.PreviouslyFlaggedForReview)
            {
                score += 25;
            }

            if (context.HasGlossaryTerm)
            {
                score += 10;
            }

            // The heaviest single factor, and the only one large enough to lift a bare label over a
            // routing threshold on its own. Measured: every string the small engine handed back
            // untranslated was a label of this kind, and their average score without this was 14 - well
            // under any usable threshold - while the prose the engine handled comfortably averaged 24.
            // Weighted to clear the default threshold of 35 from a typical short label's base of ~13,
            // because a label the app cannot explain is precisely the string a weak engine gives up on.
            if (context.IsUncoveredLabel)
            {
                score += 30;
            }
        }

        return Math.Clamp(score, 0, 100);
    }

    /// <summary>
    /// Whether <paramref name="text"/> is a bare UI label — the shape that decides
    /// <see cref="StringRiskContext.IsUncoveredLabel"/>, exposed so callers apply the same test the
    /// score does instead of inventing a second definition of "short".
    /// </summary>
    public static bool IsBareLabel(string? text) =>
        !string.IsNullOrWhiteSpace(text) && IsShortAndContextless(text);

    /// <summary>
    /// Three words or fewer, short, and with no sentence punctuation — a UI label rather than prose.
    /// </summary>
    private static bool IsShortAndContextless(string text)
    {
        if (text.Length > 24)
        {
            return false;
        }

        if (SentenceEndPattern.IsMatch(text) || text.Contains(',', StringComparison.Ordinal))
        {
            return false;
        }

        var words = 1;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                words++;
            }
        }

        return words <= 3;
    }

    /// <summary>Whether <paramref name="text"/> should go to the stronger engine at <paramref name="threshold"/>.</summary>
    public static bool IsDifficult(string? text, int threshold, StringRiskContext? context = null) =>
        Score(text, context) >= threshold;
}
