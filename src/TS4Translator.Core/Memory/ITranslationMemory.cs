using System.Globalization;

namespace TS4Translator.Core.Memory;

/// <summary>
/// Exact-match translation memory: reuses a translation whenever the exact same source text has
/// already been translated for the same locale pair, so re-translating an updated mod skips
/// everything that has not changed. This is the project's core differentiator (brief sections 24/63) —
/// fuzzy matching builds on top of this later, it does not replace it.
/// </summary>
public interface ITranslationMemory
{
    /// <summary>Looks up a previously stored exact translation, or null if none exists.</summary>
    Task<TranslationMemoryEntry?> FindExactAsync(string originalText, CultureInfo sourceLocale, CultureInfo targetLocale, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the closest previous translation for a similar-but-not-identical source text, e.g. after
    /// a small wording fix in the mod's original text (brief section 26). Returns null if nothing
    /// scores at least <paramref name="minSimilarity"/> (0.0-1.0, Levenshtein-based - see
    /// <see cref="StringSimilarity"/>). This is the fallback for near-misses; callers should try
    /// <see cref="FindExactAsync"/> first.
    /// </summary>
    Task<FuzzyMatchResult?> FindSimilarAsync(string originalText, CultureInfo sourceLocale, CultureInfo targetLocale, double minSimilarity = 0.85, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves exact or fuzzy matches for a whole provider batch. Implementations should perform
    /// one bulk read instead of reopening storage and rescanning all candidates for every string.
    /// Results remain aligned with <paramref name="originalTexts"/>; a null element means no match.
    /// </summary>
    async Task<IReadOnlyList<FuzzyMatchResult?>> FindBestMatchesAsync(
        IReadOnlyList<string> originalTexts,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        double minSimilarity = 0.85,
        CancellationToken cancellationToken = default)
    {
        var results = new List<FuzzyMatchResult?>(originalTexts.Count);
        foreach (var text in originalTexts)
        {
            var exact = await FindExactAsync(text, sourceLocale, targetLocale, cancellationToken).ConfigureAwait(false);
            results.Add(exact is not null
                ? new FuzzyMatchResult(exact, 1.0)
                : await FindSimilarAsync(text, sourceLocale, targetLocale, minSimilarity, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    /// <summary>Stores or updates the translation for this exact source text and locale pair.</summary>
    Task SaveAsync(TranslationMemoryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Browses the memory: <paramref name="query"/> matches either side of an entry, and
    /// <paramref name="provider"/> narrows to one origin (see <see cref="TranslationMemoryPolicy"/> for
    /// the names the app writes). Ordered newest first, because the most recent entries are the ones a
    /// user is most likely to be second-guessing.
    /// </summary>
    /// <returns>The page of entries, plus how many matched in total.</returns>
    Task<TranslationMemoryPage> SearchAsync(
        string? query,
        string? provider,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>How many entries exist per origin, for a summary the user can act on.</summary>
    Task<IReadOnlyList<TranslationMemoryProviderCount>> CountByProviderAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one entry, addressed by the same natural key <see cref="SaveAsync"/> writes to.
    /// </summary>
    /// <returns>True when an entry was removed.</returns>
    Task<bool> DeleteAsync(string originalText, CultureInfo sourceLocale, CultureInfo targetLocale, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every entry produced by <paramref name="provider"/>. The escape hatch for a memory filled
    /// by an engine the user has since decided they do not trust.
    /// </summary>
    /// <returns>How many entries were removed.</returns>
    Task<int> DeleteByProviderAsync(string provider, CancellationToken cancellationToken = default);

    /// <summary>Stores a completed provider batch transactionally when the backing store supports it.</summary>
    async Task SaveManyAsync(IReadOnlyList<TranslationMemoryEntry> entries, CancellationToken cancellationToken = default)
    {
        foreach (var entry in entries)
        {
            await SaveAsync(entry, cancellationToken).ConfigureAwait(false);
        }
    }
}
