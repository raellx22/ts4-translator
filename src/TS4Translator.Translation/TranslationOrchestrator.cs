using System.Globalization;
using System.Text;
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
        bool reuseTranslationMemory = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(sourceLocale);
        ArgumentNullException.ThrowIfNull(targetLocale);

        var outcomesById = new Dictionary<string, TranslationOutcome>();
        var itemsNeedingTranslation = new List<TranslationSourceItem>();

        // Not even queried when reuse is off: the lookup is a full scan of this locale pair's entries,
        // and every answer it could give would be discarded.
        var memoryMatches = reuseTranslationMemory
            ? await translationMemory.FindBestMatchesAsync(
                items.Select(item => item.Text).ToList(), sourceLocale, targetLocale, FuzzyReuseThreshold, cancellationToken).ConfigureAwait(false)
            : null;

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var memoryMatch = memoryMatches?[index];
            if (memoryMatch is { Similarity: 1.0 })
            {
                outcomesById[item.Id] = new TranslationOutcome(
                    item.Id, item.Text, memoryMatch.Entry.TranslatedText, Success: true, Issues: [], ReusedFromMemory: true);
                continue;
            }

            if (memoryMatch is not null)
            {
                var issue = new ValidationIssue(ValidationIssueCode.FuzzyMatchReused, ValidationSeverity.Warning,
                    $"Reused from a {memoryMatch.Similarity:P0} similar previous translation - please review.");
                outcomesById[item.Id] = new TranslationOutcome(
                    item.Id, item.Text, memoryMatch.Entry.TranslatedText, Success: false, Issues: [issue], ReusedFromMemory: true);
                continue;
            }

            itemsNeedingTranslation.Add(item);
        }

        var batches = provider.Capabilities is { RecommendedBatchItems: int recommendedItems, RecommendedBatchCharacters: int recommendedCharacters }
            ? new BatchBuilder(recommendedItems, recommendedCharacters).Build(itemsNeedingTranslation)
            : batchBuilder.Build(itemsNeedingTranslation);
        var maxConcurrency = Math.Clamp(provider.Capabilities.RecommendedConcurrency, 1, 8);
        var batchResults = maxConcurrency == 1
            ? await TranslateBatchesSequentiallyAsync(batches, provider, sourceLocale, targetLocale, glossaryTerms, customInstructions, cancellationToken).ConfigureAwait(false)
            : await TranslateBatchesConcurrentlyAsync(batches, provider, sourceLocale, targetLocale, glossaryTerms, customInstructions, maxConcurrency, cancellationToken).ConfigureAwait(false);

        var memoryEntriesToSave = new List<TranslationMemoryEntry>();
        foreach (var batchResult in batchResults)
        {
            foreach (var (id, outcome) in batchResult.OutcomesById)
            {
                outcomesById[id] = outcome;
            }

            memoryEntriesToSave.AddRange(batchResult.MemoryEntries);
        }

        await translationMemory.SaveManyAsync(memoryEntriesToSave, cancellationToken).ConfigureAwait(false);
        return items.Select(item => outcomesById[item.Id]).ToList();
    }

    public async Task<IReadOnlyList<TranslationOutcome>> RefineAsync(
        IReadOnlyList<RefinementSourceItem> items,
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

        if (items.Count == 0)
        {
            return [];
        }

        // No translation-memory lookup here, unlike TranslateAsync - see the interface's remarks. An
        // item being refined is already in memory, so consulting it would hand back the exact text the
        // user asked to improve.
        var sourceItems = items.Select(item => new TranslationSourceItem(item.Id, item.Text)).ToList();
        var currentTranslations = items.ToDictionary(item => item.Id, item => item.CurrentTranslation, StringComparer.Ordinal);

        var batches = provider.Capabilities is { RecommendedBatchItems: int recommendedItems, RecommendedBatchCharacters: int recommendedCharacters }
            ? new BatchBuilder(recommendedItems, recommendedCharacters).Build(sourceItems)
            : batchBuilder.Build(sourceItems);

        var maxConcurrency = Math.Clamp(provider.Capabilities.RecommendedConcurrency, 1, 8);
        var batchResults = new List<BatchTranslationResult>(batches.Count);

        // Each batch carries its own instructions (the current translations of that batch's items), so
        // the shared-instruction fast paths of TranslateAsync do not apply.
        using var gate = new SemaphoreSlim(maxConcurrency);
        var tasks = batches.Select(async batch =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var instructions = BuildRefinementInstructions(batch, currentTranslations, customInstructions, provider.Capabilities.SupportsCustomPrompt);
                return await TranslateBatchAsync(batch, provider, sourceLocale, targetLocale, glossaryTerms, instructions, cancellationToken, currentTranslations).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        });

        batchResults.AddRange(await Task.WhenAll(tasks).ConfigureAwait(false));

        var outcomesById = new Dictionary<string, TranslationOutcome>(StringComparer.Ordinal);
        var memoryEntriesToSave = new List<TranslationMemoryEntry>();
        foreach (var batchResult in batchResults)
        {
            foreach (var (id, outcome) in batchResult.OutcomesById)
            {
                outcomesById[id] = outcome;
            }

            memoryEntriesToSave.AddRange(batchResult.MemoryEntries);
        }

        await translationMemory.SaveManyAsync(memoryEntriesToSave, cancellationToken).ConfigureAwait(false);
        return items.Select(item => outcomesById[item.Id]).ToList();
    }

    /// <summary>
    /// Puts a stored draft into the same protected form as the source it belongs to.
    /// </summary>
    /// <remarks>
    /// A stored translation holds the real game tokens, because that is what gets written to the package.
    /// Handing it to a provider next to a source whose tokens are already placeholders would show the
    /// model two different notations for the same thing and invite it to emit the wrong one. Re-protecting
    /// with the source's own token map keeps both sides speaking about <c>__TS4_TOKEN_0001__</c>.
    /// </remarks>
    private static string? ProtectDraft(string? draft, ProtectedString source)
    {
        if (string.IsNullOrWhiteSpace(draft))
        {
            return null;
        }

        var protectedDraft = draft;
        foreach (var token in source.Tokens)
        {
            protectedDraft = protectedDraft.Replace(token.OriginalValue, token.Placeholder, StringComparison.Ordinal);
        }

        return protectedDraft;
    }

    /// <summary>
    /// Turns the batch's existing translations into instructions the provider can act on. A provider
    /// that cannot take a prompt (Argos, Google Translate, DeepL) gets the project's instructions
    /// unchanged and simply retranslates the source with its own engine - which is exactly what the
    /// user asked for when they picked a different engine to refine with.
    /// </summary>
    private static string? BuildRefinementInstructions(
        IReadOnlyList<TranslationSourceItem> batch,
        IReadOnlyDictionary<string, string?> currentTranslations,
        string? customInstructions,
        bool supportsCustomPrompt)
    {
        if (!supportsCustomPrompt)
        {
            return customInstructions;
        }

        var builder = new StringBuilder();
        builder.AppendLine("This is a REVISION pass, not a first translation. Each input item already has a draft translation produced by a fast, literal machine-translation engine.");
        builder.AppendLine("Improve each draft: fix wrong word senses, unnatural phrasing, wrong register, and terminology that stayed in the source language.");
        builder.AppendLine("Keep the draft's wording wherever it is already correct - do not rewrite for the sake of rewriting.");
        builder.AppendLine("The placeholder rule still applies exactly as stated above: every __TS4_TOKEN_NNNN__ must survive unchanged.");
        builder.AppendLine("Answer with the improved translation for every id, in the same response format described above.");
        builder.AppendLine();
        builder.AppendLine("Current draft translations, by id:");

        foreach (var item in batch)
        {
            var draft = currentTranslations.GetValueOrDefault(item.Id);
            builder.AppendLine(string.IsNullOrWhiteSpace(draft)
                ? $"- {item.Id}: (no draft yet - translate from scratch)"
                : $"- {item.Id}: {draft}");
        }

        if (!string.IsNullOrWhiteSpace(customInstructions))
        {
            builder.AppendLine();
            builder.AppendLine("Project style/tone instructions, which take priority over the draft:");
            builder.AppendLine(customInstructions);
        }

        return builder.ToString();
    }

    private async Task<IReadOnlyList<BatchTranslationResult>> TranslateBatchesSequentiallyAsync(
        IReadOnlyList<IReadOnlyList<TranslationSourceItem>> batches,
        ITranslationProvider provider,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        IReadOnlyList<GlossaryTerm>? glossaryTerms,
        string? customInstructions,
        CancellationToken cancellationToken)
    {
        var results = new List<BatchTranslationResult>(batches.Count);
        foreach (var batch in batches)
        {
            results.Add(await TranslateBatchAsync(batch, provider, sourceLocale, targetLocale, glossaryTerms, customInstructions, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<IReadOnlyList<BatchTranslationResult>> TranslateBatchesConcurrentlyAsync(
        IReadOnlyList<IReadOnlyList<TranslationSourceItem>> batches,
        ITranslationProvider provider,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        IReadOnlyList<GlossaryTerm>? glossaryTerms,
        string? customInstructions,
        int maxConcurrency,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(maxConcurrency);
        var tasks = batches.Select(async batch =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await TranslateBatchAsync(batch, provider, sourceLocale, targetLocale, glossaryTerms, customInstructions, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        });

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<BatchTranslationResult> TranslateBatchAsync(
        IReadOnlyList<TranslationSourceItem> batch,
        ITranslationProvider provider,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        IReadOnlyList<GlossaryTerm>? glossaryTerms,
        string? customInstructions,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? drafts = null)
    {
        var outcomesById = new Dictionary<string, TranslationOutcome>();
        var protectedByitemId = batch.ToDictionary(item => item.Id, item => tokenProtector.Protect(item.Text));
        var requestItems = batch
            .Select(item => new TranslationRequestItem(
                item.Id,
                protectedByitemId[item.Id].ProtectedText,
                ProtectDraft(drafts?.GetValueOrDefault(item.Id), protectedByitemId[item.Id])))
            .ToList();

        // Only terms that actually appear somewhere in this batch's source text are sent to the
        // provider - never the whole glossary (brief section 23).
        var relevantTerms = glossaryTerms is { Count: > 0 }
            ? glossaryTerms.Where(term => batch.Any(item => term.AppearsIn(item.Text))).ToList()
            : null;

        var results = await provider.TranslateBatchAsync(requestItems, sourceLocale, targetLocale, relevantTerms, customInstructions, cancellationToken).ConfigureAwait(false);
        var resultCountsById = results
            .GroupBy(r => r.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var memoryEntriesToSave = new List<TranslationMemoryEntry>();
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
            // Warnings are actionable review findings, not successful translations. In particular,
            // never persist an unchanged English sentence into translation memory as if it were a
            // validated pt-BR result.
            var success = issues.Count == 0;

            outcomesById[item.Id] = new TranslationOutcome(item.Id, item.Text, restoreResult.RestoredText, success, issues);

            if (success)
            {
                memoryEntriesToSave.Add(new TranslationMemoryEntry(
                    item.Text, sourceLocale, targetLocale, restoreResult.RestoredText, provider.Name, ProfileName: null, DateTimeOffset.UtcNow));
            }
        }

        return new BatchTranslationResult(outcomesById, memoryEntriesToSave);
    }

    private sealed record BatchTranslationResult(
        IReadOnlyDictionary<string, TranslationOutcome> OutcomesById,
        IReadOnlyList<TranslationMemoryEntry> MemoryEntries);
}
