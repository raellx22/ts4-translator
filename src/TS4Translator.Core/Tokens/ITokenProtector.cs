namespace TS4Translator.Core.Tokens;

/// <summary>
/// Shields non-translatable content — <c>{0.SimFirstName}</c>-style substitution tokens, markup tags,
/// and line breaks — from translation providers, and reconciles it back afterwards.
/// No translation provider is ever trusted to preserve these verbatim; this is the only component
/// allowed to reintroduce them into translated text.
/// </summary>
public interface ITokenProtector
{
    /// <summary>Replaces every protected token in <paramref name="source"/> with an opaque placeholder.</summary>
    ProtectedString Protect(string source);

    /// <summary>
    /// Reconciles placeholders in <paramref name="translatedText"/> (as returned by a translation provider)
    /// back to the original values recorded in <paramref name="protectedString"/>, by placeholder identity —
    /// never by position or count alone.
    /// </summary>
    TokenRestoreResult Restore(string translatedText, ProtectedString protectedString);
}
