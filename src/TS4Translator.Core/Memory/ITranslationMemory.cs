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

    /// <summary>Stores or updates the translation for this exact source text and locale pair.</summary>
    Task SaveAsync(TranslationMemoryEntry entry, CancellationToken cancellationToken = default);
}
