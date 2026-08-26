using TS4Translator.Core.Validation;

namespace TS4Translator.Core.Translation;

/// <param name="Id">Same identity as the originating <see cref="TranslationSourceItem"/>.</param>
/// <param name="OriginalText">The untouched source text.</param>
/// <param name="TranslatedText">
/// The best available translated text with tokens restored, even when <paramref name="Success"/> is
/// false — a flawed-but-present translation is more useful for manual review than nothing.
/// Null only when the provider produced no result at all for this id.
/// </param>
/// <param name="Success">False when any critical issue was found — callers should route these to review, not export.</param>
/// <param name="Issues">Every problem found, critical or not.</param>
public sealed record TranslationOutcome(
    string Id,
    string OriginalText,
    string? TranslatedText,
    bool Success,
    IReadOnlyList<ValidationIssue> Issues);
