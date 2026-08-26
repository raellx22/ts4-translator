using System.Globalization;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Memory;
using TS4Translator.Core.Tokens;
using TS4Translator.Core.Translation;
using TS4Translator.Core.Validation;

namespace TS4Translator.Translation;

/// <summary>
/// Default <see cref="ITranslationOrchestrator"/>. For each item: reuse an exact translation-memory
/// hit when one exists; otherwise protect tokens, batch, call the provider, reconcile results by id
/// (never by position), restore tokens, validate, and record new results back to translation memory.
/// </summary>
public sealed class TranslationOrchestrator(
    ITokenProtector tokenProtector,
    ITranslationValidator translationValidator,
    IBatchBuilder batchBuilder,
    ITranslationMemory translationMemory) : ITranslationOrchestrator
{
    /// <summary>
    /// How close a fuzzy match needs to be (brief section 26) to be reused automatically instead of
    /// calling the provider. Deliberately conservative - this is not a browsable "fuzzy suggestions"
    /// UI where a human picks from several candidates, it silently substitutes for a provider call, so
    /// it only fires for near-identical text (e.g. a punctuation or whitespace tweak in the mod's
    /// original), never for genuinely different wording. The result still comes back flagged
    /// <see cref="Core.Validation.ValidationIssueCode.FuzzyMatchReused"/> for human review either way.
    /// </summary>
    private const double FuzzyReuseThreshold = 0.97;


    public async Task<IReadOnlyList<TranslationOutcome>> TranslateAsync(
        IReadOnlyList<TranslationSourceItem> items,
        ITranslationProvider provider,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        IReadOnlyList<GlossaryTerm>? glossaryTerms = null,
        string? customInstructions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(sourceLocale);
        ArgumentNullException.ThrowIfNull(targetLocale);

        var outcomesById = new Dictionary<string, TranslationOutcome>();
        var itemsNeedingTranslation = new List<TranslationSourceItem>();

        foreach (var item in items)
        {
            var memoryHit = await translationMemory.FindExactAsync(item.Text, sourceLocale, targetLocale, cancellationToken).ConfigureAwait(false);
            if (memoryHit is not null)
            {
                outcomesById[item.Id] = new TranslationOutcome(item.Id, item.Text, memoryHit.TranslatedText, Success: true, Issues: []);
                continue;
            }

            var fuzzyHit = await translationMemory.FindSimilarAsync(item.Text, sourceLocale, targetLocale, FuzzyReuseThreshold, cancellationToken).ConfigureAwait(false);
            if (fuzzyHit is not null)
            {
                var issue = new ValidationIssue(ValidationIssueCode.FuzzyMatchReused, ValidationSeverity.Warning,
                    $"Reused from a {fuzzyHit.Similarity:P0} similar previous translation - please review.");
                outcomesById[item.Id] = new TranslationOutcome(item.Id, item.Text, fuzzyHit.Entry.TranslatedText, Success: false, Issues: [issue]);
                continue;
            }

            itemsNeedingTranslation.Add(item);
        }

        foreach (var batch in batchBuilder.Build(itemsNeedingTranslation))
        {
            await TranslateBatchAsync(batch, provider, sourceLocale, targetLocale, glossaryTerms, customInstructions, outcomesById, cancellationToken).ConfigureAwait(false);
        }

        return items.Select(item => outcomesById[item.Id]).ToList();
    }

    private async Task TranslateBatchAsync(
        IReadOnlyList<TranslationSourceItem> batch,
        ITranslationProvider provider,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        IReadOnlyList<GlossaryTerm>? glossaryTerms,
        string? customInstructions,
        Dictionary<string, TranslationOutcome> outcomesById,
        CancellationToken cancellationToken)
    {
        var protectedByitemId = batch.ToDictionary(item => item.Id, item => tokenProtector.Protect(item.Text));
        var requestItems = batch.Select(item => new TranslationRequestItem(item.Id, protectedByitemId[item.Id].ProtectedText)).ToList();

        // Only terms that actually appear somewhere in this batch's source text are sent to the
        // provider - never the whole glossary (brief section 23).
        var relevantTerms = glossaryTerms is { Count: > 0 }
            ? glossaryTerms.Where(term => batch.Any(item => term.AppearsIn(item.Text))).ToList()
            : null;

        var results = await provider.TranslateBatchAsync(requestItems, sourceLocale, targetLocale, relevantTerms, customInstructions, cancellationToken).ConfigureAwait(false);
        var resultCountsById = results
            .GroupBy(r => r.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var item in batch)
        {
            var candidates = resultCountsById.GetValueOrDefault(item.Id);

            if (candidates is null or { Count: 0 })
            {
                outcomesById[item.Id] = new TranslationOutcome(item.Id, item.Text, TranslatedText: null, Success: false,
                    Issues: [new ValidationIssue(ValidationIssueCode.ProviderResultMissing, ValidationSeverity.Critical,
                        $"The translation provider returned no result for item '{item.Id}'.")]);
                continue;
            }

            if (candidates.Count > 1)
            {
                outcomesById[item.Id] = new TranslationOutcome(item.Id, item.Text, TranslatedText: null, Success: false,
                    Issues: [new ValidationIssue(ValidationIssueCode.ProviderResultDuplicated, ValidationSeverity.Critical,
                        $"The translation provider returned {candidates.Count} results for item '{item.Id}'; refusing to guess which is correct.")]);
                continue;
            }

            // A provider can return a well-formed result for this id with a null/absent translation
            // field (e.g. a JSON response with "translation": null) - structurally valid, but nothing
            // downstream can work with it. TokenProtector.Restore/TranslationValidator.Validate both
            // reject null loudly by design (never silently treat missing data as empty), so this must
            // be caught here as a per-item failure - never let it reach them and crash the whole batch.
            if (candidates[0].TranslatedProtectedText is null)
            {
                outcomesById[item.Id] = new TranslationOutcome(item.Id, item.Text, TranslatedText: null, Success: false,
                    Issues: [new ValidationIssue(ValidationIssueCode.ProviderResultMissing, ValidationSeverity.Critical,
                        $"The translation provider returned a null translation for item '{item.Id}'.")]);
                continue;
            }

            var protectedString = protectedByitemId[item.Id];
            var restoreResult = tokenProtector.Restore(candidates[0].TranslatedProtectedText, protectedString);
            var issues = translationValidator.Validate(item.Text, restoreResult.RestoredText, restoreResult);
            var success = issues.All(i => i.Severity is not (ValidationSeverity.Error or ValidationSeverity.Critical));

            outcomesById[item.Id] = new TranslationOutcome(item.Id, item.Text, restoreResult.RestoredText, success, issues);

            if (success)
            {
                await translationMemory.SaveAsync(
                    new Core.Memory.TranslationMemoryEntry(item.Text, sourceLocale, targetLocale, restoreResult.RestoredText, provider.Name, ProfileName: null, DateTimeOffset.UtcNow),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
