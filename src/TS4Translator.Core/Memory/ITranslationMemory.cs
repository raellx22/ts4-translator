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

    /// <summary>Stores or updates the translation for this exact source text and locale pair.</summary>
    Task SaveAsync(TranslationMemoryEntry entry, CancellationToken cancellationToken = default);
}
