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
    IReadOnlyList<ValidationIssue> Validate(string originalText, string translatedText, TokenRestoreResult? tokenRestoreResult = null);
}
