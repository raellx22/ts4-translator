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
}
