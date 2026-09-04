using TS4Translator.Core.Glossary;

namespace TS4Translator.Core.Tokens;

/// <summary>
/// Shields non-translatable content — <c>{0.SimFirstName}</c>-style substitution tokens, markup tags,
/// line breaks, and enforced glossary terms — from translation providers, and reconciles it back
/// afterwards. No translation provider is ever trusted to preserve these verbatim; this is the only
/// component allowed to reintroduce them into translated text.
/// </summary>
public interface ITokenProtector
{
    /// <summary>Replaces every protected token in <paramref name="source"/> with an opaque placeholder.</summary>
    ProtectedString Protect(string source);

    /// <summary>
    /// As <see cref="Protect(string)"/>, and additionally takes the terms in <paramref name="enforcement"/>
    /// out of the text so the provider cannot render them any other way.
    /// </summary>
    /// <param name="enforcement">
    /// The glossary rules to apply as substitutions rather than as instructions. Callers decide which
    /// terms belong here — see <see cref="GlossaryEnforcement"/>.
    /// </param>
    ProtectedString Protect(string source, GlossaryEnforcement? enforcement);

    /// <summary>
    /// Reconciles placeholders in <paramref name="translatedText"/> (as returned by a translation provider)
    /// back to the values recorded in <paramref name="protectedString"/>, by placeholder identity —
    /// never by position or count alone.
    /// </summary>
    TokenRestoreResult Restore(string translatedText, ProtectedString protectedString);
}
