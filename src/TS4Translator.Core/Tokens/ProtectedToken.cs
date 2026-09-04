namespace TS4Translator.Core.Tokens;

/// <summary>
/// One placeholder, markup tag, line break, or enforced glossary term extracted from a source string
/// before translation.
/// </summary>
/// <param name="Placeholder">The opaque marker substituted into the protected text, e.g. <c>__TS4_TOKEN_0001__</c>.</param>
/// <param name="OriginalValue">The exact original substring this placeholder stands in for.</param>
/// <param name="OriginalIndex">Character offset of <paramref name="OriginalValue"/> in the original source text.</param>
/// <param name="RestoredValue">
/// What to write back in place of the placeholder, when that is not <paramref name="OriginalValue"/>.
/// </param>
/// <remarks>
/// <paramref name="RestoredValue"/> is what turns token protection into glossary <i>enforcement</i>.
/// Game code and line breaks restore to exactly what was taken out, so they leave it null. A glossary
/// term with a required translation takes "Body Selector" out of the text and puts "Seletor de Corpo"
/// back — the model never sees the English, and therefore cannot render it any other way. That is the
/// difference between asking a 4B model to honour terminology and removing the question.
/// </remarks>
public sealed record ProtectedToken(string Placeholder, string OriginalValue, int OriginalIndex, string? RestoredValue = null)
{
    /// <summary>The text this placeholder becomes when the translation is restored.</summary>
    public string ValueToRestore => RestoredValue ?? OriginalValue;

    /// <summary>Whether this token stands in for a glossary term rather than for game syntax.</summary>
    public bool IsGlossaryTerm => RestoredValue is not null;
}
