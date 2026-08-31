namespace TS4Translator.Core.Memory;

/// <param name="Entries">The requested slice, newest first.</param>
/// <param name="TotalMatches">How many entries matched the query in total, so the UI can say what it is not showing.</param>
public sealed record TranslationMemoryPage(IReadOnlyList<TranslationMemoryEntry> Entries, int TotalMatches);

/// <param name="Provider">The engine, or "manual"/"chat", that produced these entries.</param>
/// <param name="Count">How many entries it accounts for.</param>
public sealed record TranslationMemoryProviderCount(string Provider, int Count);
