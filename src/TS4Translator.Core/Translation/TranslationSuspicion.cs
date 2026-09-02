using System.Text.RegularExpressions;

namespace TS4Translator.Core.Translation;

/// <summary>Why a stored translation looks like the engine lost part of the source.</summary>
public enum SuspicionReason
{
    /// <summary>A real sentence came back in the source language.</summary>
    IdenticalToSource,

    /// <summary>A run of separator characters (<c>---------- SEX TRAITS ----------</c>) did not survive.</summary>
    LostStructure,

    /// <summary>The source carries numbers the translation does not.</summary>
    LostNumbers,

    /// <summary>Sentences that were separate in the source were merged or dropped.</summary>
    LostSentences,

    /// <summary>Far shorter than a Portuguese translation of this source has any business being.</summary>
    SeverelyShortened,

    /// <summary>A two-to-four word label came back with fewer words — the classic dropped-game-term shape.</summary>
    LostWordsInLabel,
}

/// <param name="Score">0 (nothing wrong on the surface) to 100 (several things missing at once).</param>
/// <param name="Reasons">Every finding, strongest first, so a row can say what is wrong with it.</param>
public sealed record SuspicionAssessment(int Score, IReadOnlyList<SuspicionReason> Reasons)
{
    public static SuspicionAssessment None { get; } = new(0, []);

    /// <summary>Whether this is worth putting in front of the user at the default threshold.</summary>
    public bool IsSuspect => Score >= TranslationSuspicion.DefaultThreshold;
}

/// <summary>
/// Looks at a finished translation next to its source and scores how likely it is that the engine
/// quietly dropped or flattened part of it.
/// </summary>
/// <remarks>
/// This is the app's answer to the failure that <see cref="StringDifficulty"/> explicitly cannot see and
/// <see cref="Validation.ValidationIssueCode"/> mostly does not catch: a translation that is well formed,
/// keeps every placeholder, passes validation, is stored as <c>Traduzido</c> — and is missing half of
/// what the source said. Nothing in the app pointed at those, so the only way to find them was to read
/// the mod line by line.
///
/// <b>The rules are not invented; they were read off the user's own database</b> (167 thousand strings,
/// 149 of those thousand from WickedWhims). What the low end of the length-ratio distribution actually
/// contained:
///
/// <list type="bullet">
/// <item><c>Attractiveness Tuner</c> → <c>Atraente</c>. The game concept is gone and a noun became an
/// adjective. This is the shape the user described: a label naming a buff or a tuner, flattened to the
/// nearest ordinary word.</item>
/// <item><c>I have something for you to feel :wink: I'm coming over!</c> →
/// <c>Tenho algo para você sentir:</c>. The second sentence simply is not there.</item>
/// <item><c>---------- CUM INTERACTIONS ----------</c> → <c>- Interações comuns...</c>, and around three
/// thousand more like it: the separator run destroyed, and "Cum" read as "common".</item>
/// <item><c>1 like = 1 day of not jerking off.</c> → <c>1 dia sem bater uma.</c>. Half the sentence,
/// including one of its numbers.</item>
/// </list>
///
/// Every one of those was stored as a successful translation.
///
/// Like <see cref="StringDifficulty"/> and <see cref="StringTranslatability"/>, this is a transparent
/// heuristic over text shape rather than a model: the user has to be able to look at a flagged row and
/// see instantly why it is there, and it has to run over a hundred thousand rows without being noticed.
/// It cannot see meaning either — <c>Blushing Bashfulness</c> → <c>Banalidade</c> is wrong in a way no
/// shape can detect. It finds what is <i>missing</i>, which is a different and much more tractable
/// question, and it is where the damage on this corpus actually is.
///
/// It errs towards flagging: the cost is a glance at a good translation, against shipping a broken
/// string that nobody will ever find.
/// </remarks>
public static partial class TranslationSuspicion
{
    /// <summary>
    /// The score at or above which a row is worth showing.
    /// </summary>
    /// <remarks>
    /// Calibrated on the real corpus rather than chosen: at 40, one finding of the strong kind (lost
    /// structure, lost sentences, lost numbers, or a translation under 60% of its source) is enough to
    /// flag a row on its own, while the weaker signals need corroboration. That put roughly 5% of the
    /// user's translated strings in the queue — small enough to actually work through, and containing
    /// the cases above.
    /// </remarks>
    public const int DefaultThreshold = 40;

    [GeneratedRegex(@"\{[^{}]*\}|__TS4_TOKEN_\d+__", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern { get; }

    [GeneratedRegex(@"<[^<>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex MarkupPattern { get; }

    /// <summary>Three or more of the same separator character — the mod author's own section headings.</summary>
    [GeneratedRegex(@"([-=#*_~])\1{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorRunPattern { get; }

    /// <summary>One sentence ending, however many marks it is spelled with (<c>...</c>, <c>?!?!</c>).</summary>
    [GeneratedRegex(@"[.!?…]+", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceEndPattern { get; }

    [GeneratedRegex(@"\d", RegexOptions.CultureInvariant)]
    private static partial Regex DigitPattern { get; }

    /// <summary>
    /// Scores what looks missing from <paramref name="translatedText"/> given <paramref name="originalText"/>.
    /// </summary>
    public static SuspicionAssessment Assess(string? originalText, string? translatedText)
    {
        // Nothing to judge. An untranslated row is the editor's business, not this one's.
        if (string.IsNullOrWhiteSpace(originalText) || string.IsNullOrWhiteSpace(translatedText))
        {
            return SuspicionAssessment.None;
        }

        var kind = StringTranslatability.Classify(originalText);

        // A tuning identifier coming back unchanged is the correct answer, and everything below would
        // read it as catastrophic loss.
        if (kind == StringKind.Code)
        {
            return SuspicionAssessment.None;
        }

        var score = 0;
        var reasons = new List<(SuspicionReason Reason, int Weight)>(4);

        void Add(SuspicionReason reason, int weight)
        {
            score += weight;
            reasons.Add((reason, weight));
        }

        // Structure first, and on the raw text: the separator runs live outside the sentence, and this is
        // the highest-precision signal on the corpus - a heading that lost its rule was destroyed
        // essentially every time.
        var sourceSeparators = SeparatorRunPattern.Count(originalText);
        if (sourceSeparators > 0 && SeparatorRunPattern.Count(translatedText) < sourceSeparators)
        {
            Add(SuspicionReason.LostStructure, 45);
        }

        // Everything else compares what a translator was actually asked to render: placeholders and
        // markup are copied verbatim and would otherwise dominate the length of a short string.
        var source = VisibleBody(originalText);
        var translated = VisibleBody(translatedText);

        if (source.Length == 0)
        {
            return Build(score, reasons);
        }

        // Narrower than StringKind.Prose on purpose. The classifier calls anything with sentence
        // punctuation prose, which makes "Strip Poker!" and "Uh Oh!" prose - and those coming back
        // unchanged is the right answer, not a failure (DEC-035 says so in as many words). A real
        // sentence has more than three words; below that, an unchanged string is a title.
        if (kind == StringKind.Prose
            && CountWords(source) > 3
            && string.Equals(source, translated, StringComparison.OrdinalIgnoreCase))
        {
            Add(SuspicionReason.IdenticalToSource, 60);
        }

        // Counted, not matched: "1 like = 1 day" losing one of its two ones is the case, and both are
        // the digit "1", so comparing which digits appear would see nothing wrong.
        var sourceDigits = DigitPattern.Count(source);
        if (sourceDigits > 0 && DigitPattern.Count(translated) < sourceDigits)
        {
            Add(SuspicionReason.LostNumbers, 35);
        }

        var sourceSentences = SentenceEndPattern.Count(source);
        var translatedSentences = SentenceEndPattern.Count(translated);
        if (sourceSentences > 1 && translatedSentences < sourceSentences)
        {
            // Two sentences becoming one is the common case and already worth a look; losing more than
            // that is not a stylistic merge, it is text that is gone.
            Add(SuspicionReason.LostSentences, sourceSentences - translatedSentences >= 2 ? 45 : 35);
        }

        // Portuguese runs longer than English far more often than shorter, so a translation that is much
        // shorter than its source is the single broadest signal of dropped content. Short strings are
        // excluded: at ten characters the ratio swings wildly on one word and says nothing.
        if (source.Length >= 20)
        {
            var ratio = (double)translated.Length / source.Length;
            if (ratio < 0.60)
            {
                Add(SuspicionReason.SeverelyShortened, 40);
            }
            else if (ratio < 0.72)
            {
                // On its own this is usually just Portuguese being terse; it earns a place in the queue
                // only when something else is wrong too.
                Add(SuspicionReason.SeverelyShortened, 20);
            }
        }

        // The user's own example, generalised. "Sex Tuner" needs three words in Portuguese
        // ("Ajustador de Sexo"); a two-word label that comes back as one word usually lost the half that
        // named the game concept. Weak on its own - "Run Away" really is "Fuja!" - so one lost word is a
        // nudge and two is a finding.
        if (kind == StringKind.Term)
        {
            var sourceWords = CountWords(source);
            var translatedWords = CountWords(translated);
            if (sourceWords is >= 2 and <= 4 && translatedWords < sourceWords)
            {
                Add(SuspicionReason.LostWordsInLabel, sourceWords - translatedWords >= 2 ? 40 : 22);
            }
        }

        return Build(score, reasons);
    }

    /// <summary>Convenience for the common "is this worth showing?" question.</summary>
    public static bool IsSuspect(string? originalText, string? translatedText) =>
        Assess(originalText, translatedText).IsSuspect;

    private static SuspicionAssessment Build(int score, List<(SuspicionReason Reason, int Weight)> reasons)
    {
        if (reasons.Count == 0)
        {
            return SuspicionAssessment.None;
        }

        reasons.Sort((left, right) => right.Weight.CompareTo(left.Weight));
        return new SuspicionAssessment(Math.Clamp(score, 0, 100), [.. reasons.Select(pair => pair.Reason)]);
    }

    /// <summary>What a translator was actually asked to render: no placeholders, no markup.</summary>
    private static string VisibleBody(string text) =>
        MarkupPattern.Replace(PlaceholderPattern.Replace(text, " "), " ").Trim();

    /// <summary>
    /// Words, counting a slash or dash as a break.
    /// </summary>
    /// <remarks>
    /// Because "Native / Override" becoming "Nativo/Substituir" is not a lost word, and counting on
    /// whitespace alone said it was. Punctuation spacing changes between languages and must not read as
    /// missing content.
    /// </remarks>
    private static int CountWords(string text) =>
        text.Split([' ', '\t', '\n', '\r', '/', '-', '–', '—'], StringSplitOptions.RemoveEmptyEntries).Length;
}
