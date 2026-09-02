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
    /// <param name="reuseTranslationMemory">
    /// False sends every item to <paramref name="provider"/> even when memory already holds an answer.
    /// </param>
    /// <remarks>
    /// Reuse is on by default and is the whole point of the memory — but it is also why a bad run is so
    /// hard to get rid of: the memory outlives the project it was produced in, so deleting the project
    /// and importing the mod again replays the same wrong translations without touching an engine. Any
    /// "do this over" or "try another model" path has to be able to switch it off, and results are still
    /// written back afterwards, so a fresh run replaces what it disagreed with.
    /// </remarks>
    Task<IReadOnlyList<TranslationOutcome>> TranslateAsync(
        IReadOnlyList<TranslationSourceItem> items,
        ITranslationProvider provider,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        IReadOnlyList<GlossaryTerm>? glossaryTerms = null,
        string? customInstructions = null,
        bool reuseTranslationMemory = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-runs items through <paramref name="provider"/> to improve an existing translation, rather
    /// than to produce a first one.
    /// </summary>
    /// <remarks>
    /// Deliberately **not** the same call as <see cref="TranslateAsync"/> with different arguments:
    /// that path consults translation memory first, and every item being refined is already in memory
    /// by definition, so it would return the very translation the user is trying to improve and never
    /// reach the provider at all. Refinement therefore skips the memory lookup. Token protection,
    /// id-based reconciliation and validation all still apply, and a successful result still updates
    /// memory — the improved text should be what future strings reuse.
    /// </remarks>
    Task<IReadOnlyList<TranslationOutcome>> RefineAsync(
        IReadOnlyList<RefinementSourceItem> items,
        ITranslationProvider provider,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        IReadOnlyList<GlossaryTerm>? glossaryTerms = null,
        string? customInstructions = null,
        CancellationToken cancellationToken = default);
}
