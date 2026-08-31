using System.Globalization;
using TS4Translator.Core.Memory;

namespace TS4Translator.Translation.Tests;

/// <summary>In-memory <see cref="ITranslationMemory"/> test double — no SQLite required for orchestrator tests.</summary>
public sealed class FakeTranslationMemory : ITranslationMemory
{
    private readonly Dictionary<(string Text, string Source, string Target), TranslationMemoryEntry> _entries = [];

    public int SaveCallCount { get; private set; }

    public Task<TranslationMemoryEntry?> FindExactAsync(string originalText, CultureInfo sourceLocale, CultureInfo targetLocale, CancellationToken cancellationToken = default)
    {
        _entries.TryGetValue((originalText.Trim(), sourceLocale.Name, targetLocale.Name), out var entry);
        return Task.FromResult(entry);
    }

    public Task<FuzzyMatchResult?> FindSimilarAsync(string originalText, CultureInfo sourceLocale, CultureInfo targetLocale, double minSimilarity = 0.85, CancellationToken cancellationToken = default)
    {
        var normalized = originalText.Trim();
        FuzzyMatchResult? best = null;

        foreach (var ((text, source, target), entry) in _entries)
        {
            if (source != sourceLocale.Name || target != targetLocale.Name)
            {
                continue;
            }

            var similarity = StringSimilarity.Ratio(normalized, text);
            if (similarity >= minSimilarity && (best is null || similarity > best.Similarity))
            {
                best = new FuzzyMatchResult(entry, similarity);
            }
        }

        return Task.FromResult(best);
    }

    public Task SaveAsync(TranslationMemoryEntry entry, CancellationToken cancellationToken = default)
    {
        SaveCallCount++;
        _entries[(entry.OriginalText.Trim(), entry.SourceLocale.Name, entry.TargetLocale.Name)] = entry;
        return Task.CompletedTask;
    }

    // ---- browsing and housekeeping: enough for the orchestrator tests, which never read them ----

    public Task<TranslationMemoryPage> SearchAsync(string? query, string? provider, int skip, int take, CancellationToken cancellationToken = default)
    {
        var matches = _entries.Values
            .Where(entry => string.IsNullOrWhiteSpace(query)
                || entry.OriginalText.Contains(query, StringComparison.OrdinalIgnoreCase)
                || entry.TranslatedText.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Where(entry => string.IsNullOrWhiteSpace(provider) || entry.Provider == provider)
            .ToList();

        return Task.FromResult(new TranslationMemoryPage([.. matches.Skip(skip).Take(take)], matches.Count));
    }

    public Task<IReadOnlyList<TranslationMemoryProviderCount>> CountByProviderAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TranslationMemoryProviderCount>>(
            [.. _entries.Values.CountBy(entry => entry.Provider).Select(pair => new TranslationMemoryProviderCount(pair.Key, pair.Value))]);

    public Task<bool> DeleteAsync(string originalText, CultureInfo sourceLocale, CultureInfo targetLocale, CancellationToken cancellationToken = default) =>
        Task.FromResult(_entries.Remove((originalText.Trim(), sourceLocale.Name, targetLocale.Name)));

    public Task<int> DeleteByProviderAsync(string provider, CancellationToken cancellationToken = default)
    {
        var doomed = _entries.Where(pair => pair.Value.Provider == provider).Select(pair => pair.Key).ToList();
        foreach (var key in doomed)
        {
            _entries.Remove(key);
        }

        return Task.FromResult(doomed.Count);
    }
}
