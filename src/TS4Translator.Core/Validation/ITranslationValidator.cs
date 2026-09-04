using TS4Translator.Core.Glossary;
using TS4Translator.Core.Tokens;

namespace TS4Translator.Core.Validation;

/// <summary>
/// Independent quality/safety checks run on a translated string after token restoration.
/// This never blocks export by itself — callers decide what to do with the returned issues
/// (e.g. surface only Warning-or-above in the editor, treat Critical as export blockers).
/// </summary>
public interface ITranslationValidator
{
    /// <param name="originalText">The original (source-language) string.</param>
    /// <param name="translatedText">The translated string, with tokens already restored.</param>
    /// <param name="tokenRestoreResult">
    /// The result of <see cref="ITokenProtector.Restore"/> for this string, when available. Its issues
    /// are folded into the returned list so callers have one place to look.
    /// </param>
    /// <param name="glossary">
    /// The terms in play, when the caller has them. Every rule that applies to <paramref name="originalText"/>
    /// is checked against <paramref name="translatedText"/> — the check that used to exist only for the
    /// chat-window import, so an engine the app drives itself could ignore the glossary unnoticed.
    /// </param>
    IReadOnlyList<ValidationIssue> Validate(
        string originalText,
        string translatedText,
        TokenRestoreResult? tokenRestoreResult = null,
        GlossaryIndex? glossary = null);
}
