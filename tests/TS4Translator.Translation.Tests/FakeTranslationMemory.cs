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

    public Task SaveAsync(TranslationMemoryEntry entry, CancellationToken cancellationToken = default)
    {
        SaveCallCount++;
        _entries[(entry.OriginalText.Trim(), entry.SourceLocale.Name, entry.TargetLocale.Name)] = entry;
        return Task.CompletedTask;
    }
}
