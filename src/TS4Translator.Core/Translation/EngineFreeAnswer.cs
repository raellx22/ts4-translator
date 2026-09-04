using TS4Translator.Core.Glossary;

namespace TS4Translator.Core.Translation;

/// <param name="Text">What to store for this string.</param>
/// <param name="FromGlossary">
/// True when a glossary term supplied it; false when the string is not translatable text at all and is
/// kept as-is.
/// </param>
public sealed record EngineFreeResult(string Text, bool FromGlossary);

/// <summary>
/// Whether a string can be answered without spending an engine on it, and with what.
/// </summary>
/// <remarks>
/// One definition, because two callers need the same answer and must not disagree:
/// <c>TranslationOrchestrator</c> acts on it, and the routing preview subtracts it from the estimate it
/// shows before a run. A preview that counts strings the run will never send is a cost estimate that
/// overstates itself, and the user decides whether to press the button based on that number.
/// </remarks>
public static class EngineFreeAnswer
{
    /// <returns>The answer, or null when this string genuinely needs an engine.</returns>
    public static EngineFreeResult? For(string? text, GlossaryIndex? glossary)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        // The glossary is consulted first, because an explicit rule outranks a guess about shape. It is
        // not a hypothetical ordering: StringTranslatability reads "WickedWhims" as an identifier — the
        // capital inside the word is exactly what marks camelCase tuning names — so asking the shape
        // first would answer every mod name as "internal identifier, kept as-is". The stored text is the
        // same either way; what changes is that the user is told a glossary rule fired, and a rule they
        // wrote for a camelCase name that *does* need translating actually applies.
        if (glossary?.WholeStringMatch(text) is { } term)
        {
            // "Never translate" covering the whole string means the string is the name; the right output
            // is the input, with whatever casing the mod used.
            var translated = term.NeverTranslate ? text : term.Translation;
            if (!string.IsNullOrWhiteSpace(translated))
            {
                return new EngineFreeResult(translated, FromGlossary: true);
            }
        }

        // Not human text: a tuning identifier, a bare number, nothing but placeholders. Keeping it
        // unchanged is the correct answer, and StringTranslatability's own documentation has said so
        // since it was written — while the path that runs the engines consulted it nowhere.
        return StringTranslatability.IsWorthTranslating(text) ? null : new EngineFreeResult(text, FromGlossary: false);
    }

    /// <summary>Whether an engine would be called for this string.</summary>
    public static bool NeedsEngine(string? text, GlossaryIndex? glossary) => For(text, glossary) is null;
}
