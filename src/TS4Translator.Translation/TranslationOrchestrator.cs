using System.Globalization;
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
    public async Task<IReadOnlyList<TranslationOutcome>> TranslateAsync(
        IReadOnlyList<TranslationSourceItem> items,
        ITranslationProvider provider,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
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
            }
            else
            {
                itemsNeedingTranslation.Add(item);
            }
        }

        foreach (var batch in batchBuilder.Build(itemsNeedingTranslation))
        {
            await TranslateBatchAsync(batch, provider, sourceLocale, targetLocale, outcomesById, cancellationToken).ConfigureAwait(false);
        }

        return items.Select(item => outcomesById[item.Id]).ToList();
    }

    private async Task TranslateBatchAsync(
        IReadOnlyList<TranslationSourceItem> batch,
        ITranslationProvider provider,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        Dictionary<string, TranslationOutcome> outcomesById,
        CancellationToken cancellationToken)
    {
        var protectedByitemId = batch.ToDictionary(item => item.Id, item => tokenProtector.Protect(item.Text));
        var requestItems = batch.Select(item => new TranslationRequestItem(item.Id, protectedByitemId[item.Id].ProtectedText)).ToList();

        var results = await provider.TranslateBatchAsync(requestItems, sourceLocale, targetLocale, cancellationToken).ConfigureAwait(false);
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
