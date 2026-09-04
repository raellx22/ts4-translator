using System.Globalization;
using System.Text;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Memory;
using TS4Translator.Core.Tokens;
using TS4Translator.Core.Translation;
using TS4Translator.Core.Validation;

namespace TS4Translator.Translation;

/// <summary>
/// Default <see cref="ITranslationOrchestrator"/>. For each item: skip what is not text at all; answer
/// from the glossary when the glossary already knows the whole string; reuse an exact translation-memory
/// hit when one exists; otherwise protect tokens and enforced terminology, batch, call the provider,
/// reconcile results by id (never by position), restore, validate, and record new results back to
/// translation memory.
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

    /// <summary>
    /// How many glossary terms one batch may carry into the prompt.
    /// </summary>
    /// <remarks>
    /// A ceiling, not a target, and it exists because the glossary grew from 386 terms to over five thousand when it
    /// started being harvested from community translations. Measured over 3.000 real strings, a batch of
    /// 100 matches 44 terms on average and 100 at the worst; 80 covers 99% of what any batch would send.
    ///
    /// The reason for a ceiling at all is a failure already observed: at 387 terms, translategemma-4b
    /// answered three of 120 strings by translating the <i>instructions</i> instead of the text
    /// (DEC-045). <see cref="Providers.LocalModel.PromptEcho"/> catches that now, but a prompt that
    /// cannot balloon is better than one that is caught after ballooning.
    ///
    /// Which 80, however, is no longer "the longest in the batch" — see
    /// <see cref="GlossaryIndex.SelectForBatch"/>. Sorting the batch's matches by length and cutting
    /// dropped a short term that was the only rule for one string because other strings happened to
    /// carry longer ones, which is exactly backwards.
    /// </remarks>
    private const int MaximumGlossaryTerms = 80;

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

        var glossary = GlossaryIndex.For(glossaryTerms);
        var outcomesById = new Dictionary<string, TranslationOutcome>(StringComparer.Ordinal);
        var itemsNeedingTranslation = new List<TranslationSourceItem>();

        // Two passes rather than one, because the three sources of a free answer do not rank equally.
        //
        // A string that is not text at all is settled here and asks nothing of anything else: memory has
        // no business holding a translation for a tuning identifier, and a lookup for one is work with no
        // possible use. A string the glossary answers whole is *not* settled yet — it waits, because
        // translation memory holds what a human approved for that exact string, and a term rule
        // harvested from the community must never overrule the user's own correction. That ordering is
        // the same one DEC-048 applied when the list was built.
        var settledLocally = new bool[items.Count];
        var glossaryAnswers = new string?[items.Count];

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (EngineFreeAnswer.For(item.Text, glossary) is not { } answer)
            {
                continue;
            }

            if (answer.FromGlossary)
            {
                glossaryAnswers[index] = answer.Text;
                continue;
            }

            outcomesById[item.Id] = new TranslationOutcome(item.Id, item.Text, answer.Text, Success: true,
                Issues: [new ValidationIssue(ValidationIssueCode.NotTranslatable, ValidationSeverity.Info,
                    "É um identificador interno do mod, não um texto de tela. Mantido como está, sem gastar motor.")]);
            settledLocally[index] = true;
        }

        // Not even queried when reuse is off: the lookup is a scan of this locale pair's entries, and
        // every answer it could give would be discarded. Items already settled are left out of the
        // request as well - asking memory about a tuning identifier is work with no possible use.
        var lookupTexts = new List<string>(items.Count);
        var lookupIndexes = new List<int>(items.Count);
        if (reuseTranslationMemory)
        {
            for (var index = 0; index < items.Count; index++)
            {
                if (!settledLocally[index])
                {
                    lookupTexts.Add(items[index].Text);
                    lookupIndexes.Add(index);
                }
            }
        }

        var memoryMatches = lookupTexts.Count > 0
            ? await translationMemory.FindBestMatchesAsync(
                lookupTexts, sourceLocale, targetLocale, FuzzyReuseThreshold, cancellationToken).ConfigureAwait(false)
            : null;

        var matchByItemIndex = new Dictionary<int, FuzzyMatchResult?>(lookupIndexes.Count);
        for (var index = 0; memoryMatches is not null && index < lookupIndexes.Count; index++)
        {
            matchByItemIndex[lookupIndexes[index]] = memoryMatches[index];
        }

        for (var index = 0; index < items.Count; index++)
        {
            if (settledLocally[index])
            {
                continue;
            }

            var item = items[index];
            var memoryMatch = matchByItemIndex.GetValueOrDefault(index);

            if (memoryMatch is { Similarity: 1.0 })
            {
                outcomesById[item.Id] = new TranslationOutcome(
                    item.Id, item.Text, memoryMatch.Entry.TranslatedText, Success: true, Issues: [], ReusedFromMemory: true);
                continue;
            }

            // Ahead of a fuzzy hit, which is a near-miss the user is asked to check, and ahead of any
            // engine: the glossary's answer for a whole string is exact by construction.
            if (glossaryAnswers[index] is { } fromGlossary)
            {
                outcomesById[item.Id] = new TranslationOutcome(
                    item.Id, item.Text, fromGlossary, Success: true, Issues: [], AppliedFromGlossary: true);
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

        var batches = BuildBatches(itemsNeedingTranslation, provider);
        var enforcement = GlossaryEnforcement.For(glossary, provider.Capabilities.SupportsGlossary);
        var maxConcurrency = Math.Clamp(provider.Capabilities.RecommendedConcurrency, 1, 8);
        var context = new BatchContext(provider, sourceLocale, targetLocale, glossary, enforcement, customInstructions);

        var batchResults = maxConcurrency == 1
            ? await TranslateBatchesSequentiallyAsync(batches, context, cancellationToken).ConfigureAwait(false)
            : await TranslateBatchesConcurrentlyAsync(batches, context, maxConcurrency, cancellationToken).ConfigureAwait(false);

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
        var glossary = GlossaryIndex.For(glossaryTerms);
        var currentTranslations = items.ToDictionary(item => item.Id, item => item.CurrentTranslation, StringComparer.Ordinal);
        var outcomesById = new Dictionary<string, TranslationOutcome>(StringComparer.Ordinal);

        // A tuning identifier has nothing to refine: the correct rendering is the identifier, so the
        // best a revision pass could do is leave it alone and the likely outcome is damage. Unlike
        // TranslateAsync this does *not* consult the glossary for a whole-string answer — the user
        // picked an engine and asked it to improve this line, and answering from a term rule instead
        // would be ignoring the request they actually made.
        var sourceItems = new List<TranslationSourceItem>(items.Count);
        foreach (var item in items)
        {
            if (StringTranslatability.IsWorthTranslating(item.Text))
            {
                sourceItems.Add(new TranslationSourceItem(item.Id, item.Text));
                continue;
            }

            outcomesById[item.Id] = new TranslationOutcome(item.Id, item.Text, item.Text, Success: true,
                Issues: [new ValidationIssue(ValidationIssueCode.NotTranslatable, ValidationSeverity.Info,
                    "É um identificador interno do mod, não um texto de tela. Mantido como está, sem gastar motor.")]);
        }

        if (sourceItems.Count == 0)
        {
            return items.Select(item => outcomesById[item.Id]).ToList();
        }

        var batches = BuildBatches(sourceItems, provider);
        var enforcement = GlossaryEnforcement.For(glossary, provider.Capabilities.SupportsGlossary);
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
                var context = new BatchContext(provider, sourceLocale, targetLocale, glossary, enforcement, instructions);
                return await TranslateBatchAsync(batch, context, cancellationToken, currentTranslations).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        });

        batchResults.AddRange(await Task.WhenAll(tasks).ConfigureAwait(false));

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

    private IReadOnlyList<IReadOnlyList<TranslationSourceItem>> BuildBatches(
        IReadOnlyList<TranslationSourceItem> items, ITranslationProvider provider) =>
        provider.Capabilities is { RecommendedBatchItems: int recommendedItems, RecommendedBatchCharacters: int recommendedCharacters }
            ? new BatchBuilder(recommendedItems, recommendedCharacters).Build(items)
            : batchBuilder.Build(items);

    /// <summary>Everything a batch needs that does not change from one batch to the next.</summary>
    private readonly record struct BatchContext(
        ITranslationProvider Provider,
        CultureInfo SourceLocale,
        CultureInfo TargetLocale,
        GlossaryIndex Glossary,
        GlossaryEnforcement? Enforcement,
        string? CustomInstructions);

    /// <summary>
    /// Puts a stored draft into the same protected form as the source it belongs to.
    /// </summary>
    /// <remarks>
    /// A stored translation holds the real game tokens, because that is what gets written to the package.
    /// Handing it to a provider next to a source whose tokens are already placeholders would show the
    /// model two different notations for the same thing and invite it to emit the wrong one. Re-protecting
    /// with the source's own token map keeps both sides speaking about <c>__TS4_TOKEN_0001__</c>.
    ///
    /// A glossary token is matched on what it restores to, not on what was taken out: the draft is in the
    /// target language, so the text standing where the term goes is the Portuguese the glossary requires,
    /// not the English the source had.
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
            protectedDraft = protectedDraft.Replace(token.ValueToRestore, token.Placeholder, StringComparison.Ordinal);
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
        builder.AppendLine("The placeholder rule still applies exactly as stated above: every placeholder must survive unchanged.");
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
        BatchContext context,
        CancellationToken cancellationToken)
    {
        var results = new List<BatchTranslationResult>(batches.Count);
        foreach (var batch in batches)
        {
            results.Add(await TranslateBatchAsync(batch, context, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<IReadOnlyList<BatchTranslationResult>> TranslateBatchesConcurrentlyAsync(
        IReadOnlyList<IReadOnlyList<TranslationSourceItem>> batches,
        BatchContext context,
        int maxConcurrency,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(maxConcurrency);
        var tasks = batches.Select(async batch =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await TranslateBatchAsync(batch, context, cancellationToken).ConfigureAwait(false);
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
        BatchContext context,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? drafts = null)
    {
        var outcomesById = new Dictionary<string, TranslationOutcome>(StringComparer.Ordinal);
        var protectedByitemId = batch.ToDictionary(
            item => item.Id,
            item => tokenProtector.Protect(item.Text, context.Enforcement),
            StringComparer.Ordinal);

        var requestItems = batch
            .Select(item => new TranslationRequestItem(
                item.Id,
                protectedByitemId[item.Id].ProtectedText,
                ProtectDraft(drafts?.GetValueOrDefault(item.Id), protectedByitemId[item.Id])))
            .ToList();

        // Only terms that actually appear somewhere in this batch's source text are sent to the
        // provider - never the whole glossary (brief section 23) - and only to a provider that can act
        // on them at all. Argos, Google Translate and DeepL take no prompt: they used to be handed the
        // selection anyway and drop it, so the app paid for the matching and the glossary had no effect
        // on the cheapest lane of the routing plan. Their terminology is enforced by substitution
        // instead, in Protect above.
        //
        // Null and empty are different answers, deliberately: null means the run has no glossary at
        // all, empty means it has one and none of it applies here. Providers treat both the same, but a
        // test double reading the argument can tell the two apart, which is what pins the filtering.
        var relevantTerms = context.Glossary.Count > 0 && context.Provider.Capabilities.SupportsGlossary
            ? context.Glossary.SelectForBatch([.. batch.Select(item => item.Text)], MaximumGlossaryTerms)
            : null;

        var results = await context.Provider.TranslateBatchAsync(
            requestItems, context.SourceLocale, context.TargetLocale, relevantTerms, context.CustomInstructions, cancellationToken).ConfigureAwait(false);

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
            var issues = translationValidator.Validate(item.Text, restoreResult.RestoredText, restoreResult, context.Glossary);

            // Warnings are actionable review findings, not successful translations. In particular,
            // never persist an unchanged English sentence into translation memory as if it were a
            // validated pt-BR result. Info is an observation and does not disqualify anything.
            var success = !issues.Any(issue => issue.Severity >= ValidationSeverity.Warning);

            outcomesById[item.Id] = new TranslationOutcome(item.Id, item.Text, restoreResult.RestoredText, success, issues);

            if (success)
            {
                memoryEntriesToSave.Add(new TranslationMemoryEntry(
                    item.Text, context.SourceLocale, context.TargetLocale, restoreResult.RestoredText, context.Provider.Name, ProfileName: null, DateTimeOffset.UtcNow));
            }
        }

        return new BatchTranslationResult(outcomesById, memoryEntriesToSave);
    }

    private sealed record BatchTranslationResult(
        IReadOnlyDictionary<string, TranslationOutcome> OutcomesById,
        IReadOnlyList<TranslationMemoryEntry> MemoryEntries);
}
