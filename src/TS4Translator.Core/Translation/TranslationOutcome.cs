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
/// <param name="ReusedFromMemory">
/// True when no engine was called for this item and the text came out of translation memory.
/// </param>
/// <param name="AppliedFromGlossary">
/// True when no engine was called because a glossary term covered the whole string.
/// </param>
/// <remarks>
/// <paramref name="ReusedFromMemory"/> exists because a reused line and a freshly translated one were
/// indistinguishable to the caller, and therefore to the user: a run reporting "73 traduzidas" could
/// have called no engine at all. That is the difference between "the new model did this" and "you are
/// looking at the previous model's work again", which is precisely the question someone comparing
/// engines is asking.
///
/// <paramref name="AppliedFromGlossary"/> exists for the same reason and answers a sharper version of
/// it: a run that reports a thousand translations may have spent nothing on several hundred of them,
/// because the glossary already held the whole label. That is the app working exactly as intended, and
/// it should be visible rather than inferred from a suspiciously small bill.
/// </remarks>
public sealed record TranslationOutcome(
    string Id,
    string OriginalText,
    string? TranslatedText,
    bool Success,
    IReadOnlyList<ValidationIssue> Issues,
    bool ReusedFromMemory = false,
    bool AppliedFromGlossary = false);
