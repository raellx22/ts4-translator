using System.Globalization;
using TS4Translator.Core.Glossary;

namespace TS4Translator.Core.Translation;

/// <summary>
/// Runs the full per-item pipeline described in brief section 10/11: skip items already in
/// translation memory, protect tokens, call the provider, reconcile results by id, restore tokens,
/// validate, and record new results back to translation memory. Never trusts the provider's response
/// order or count.
/// </summary>
public interface ITranslationOrchestrator
{
    /// <param name="glossaryTerms">
    /// Candidate terms (global + project-specific) to enforce. Only the ones that actually appear in
    /// each batch are sent to the provider (brief section 23 - never send the whole glossary).
    /// </param>
    /// <param name="customInstructions">
    /// The project's Translation Profile free-text instructions (brief section 22), forwarded to the
    /// provider unchanged on every batch of this run - unlike <paramref name="glossaryTerms"/>, this is
    /// not content-dependent, so there is nothing to filter per batch.
    /// </param>
    Task<IReadOnlyList<TranslationOutcome>> TranslateAsync(
        IReadOnlyList<TranslationSourceItem> items,
        ITranslationProvider provider,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        IReadOnlyList<GlossaryTerm>? glossaryTerms = null,
        string? customInstructions = null,
        CancellationToken cancellationToken = default);
}
