using System.Text.RegularExpressions;

namespace TS4Translator.Core.Translation;

/// <summary>What kind of thing a source string is, from the point of view of someone translating it.</summary>
public enum StringKind
{
    /// <summary>
    /// Not human text at all: an internal identifier, a bare number, or nothing but placeholders.
    /// Sending it to a translator is wasted money and getting it back unchanged is the correct answer.
    /// </summary>
    Code,

    /// <summary>
    /// A short label, name or term. It may legitimately come back identical - brand names, "Sims",
    /// "Bar", a mod's own prefix - so an unchanged term is worth a note, never an alarm.
    /// </summary>
    Term,

    /// <summary>A real sentence. If this comes back unchanged, something went wrong.</summary>
    Prose,
}

/// <summary>
/// Tells apart the three kinds of string a Sims 4 STBL actually holds, so the app stops treating them
/// all as translatable text.
/// </summary>
/// <remarks>
/// This exists because of a real import. A thousand strings of "[Kritical] Dreams Of Surrender" went to
/// a chat model and came back perfectly - every id intact, no placeholder lost - and the app reported
/// 134 warnings, which read like a broken file. 128 of those were strings such as
/// <c>debug_savedActor2</c>, <c>craftGags1_cd</c> and <c>publicUseCd</c>: tuning identifiers that live
/// in the string table but are never shown to a player. The model had been right to leave them alone.
/// One warning out of 134 was real, and it was buried.
///
/// So the rule the app now follows is: <see cref="StringKind.Code"/> never leaves the app,
/// <see cref="StringKind.Term"/> coming back unchanged is an observation, and only
/// <see cref="StringKind.Prose"/> coming back unchanged is a warning.
///
/// Like <see cref="StringDifficulty"/> this is a transparent heuristic over text shape, not a model:
/// the user has to be able to look at a string the app skipped and see immediately why. It errs
/// towards <see cref="StringKind.Term"/> whenever the shape is ambiguous, because the cost of
/// misjudging is asymmetric - a needless note costs a glance, a string wrongly withheld from the
/// translator ships untranslated.
/// </remarks>
public static partial class StringTranslatability
{
    /// <summary>Sims 4 placeholders, e.g. <c>{0.SimFirstName}</c>, and the app's protected form.</summary>
    [GeneratedRegex(@"\{[^{}]*\}|__TS4_TOKEN_\d+__", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern { get; }

    /// <summary>Markup the game renders, e.g. <c>&lt;font color='#AB0C10'&gt;</c>.</summary>
    [GeneratedRegex(@"<[^<>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex MarkupPattern { get; }

    /// <summary><c>snake_case</c>, <c>camelCase</c>, or a word with digits glued to it - never how a UI label is written.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9_]*(_[A-Za-z0-9_]*|[a-z][A-Z]|[A-Za-z][0-9]|[0-9][A-Za-z])[A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierShapePattern { get; }

    [GeneratedRegex(@"[.!?,;:]", RegexOptions.CultureInvariant)]
    private static partial Regex SentencePunctuationPattern { get; }

    public static StringKind Classify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return StringKind.Code;
        }

        // What a translator would actually be handed: placeholders and markup are copied verbatim, so
        // they say nothing about whether there is anything here to translate.
        var body = MarkupPattern.Replace(PlaceholderPattern.Replace(text, " "), " ").Trim();

        if (!body.Any(char.IsLetter))
        {
            return StringKind.Code;
        }

        var words = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        // A single token is where identifiers live. "highWaistBelt", "craftGags1_cd" and "var2" are
        // code; "Flogger" and "Kritical" are terms that may or may not need translating.
        if (words.Length == 1)
        {
            return IdentifierShapePattern.IsMatch(words[0]) ? StringKind.Code : StringKind.Term;
        }

        // Several tokens that are *all* identifier-shaped is still code - a tuning value that happens to
        // have a space in it. One ordinary word among them is enough to make it text again, which is why
        // "notif cd" stays a term: guessing wrong there would withhold a real label from the translator.
        if (words.All(IdentifierShapePattern.IsMatch))
        {
            return StringKind.Code;
        }

        // Punctuation or length is what separates a sentence from a label. Both are needed: "Lock Inside
        // (Punishment)" is a label with four words, and "Really?" is prose with one.
        return SentencePunctuationPattern.IsMatch(body) || words.Length > 4 ? StringKind.Prose : StringKind.Term;
    }

    /// <summary>
    /// Whether it is worth spending a translator - paid, local or a chat window - on this string.
    /// </summary>
    public static bool IsWorthTranslating(string? text) => Classify(text) != StringKind.Code;
}
