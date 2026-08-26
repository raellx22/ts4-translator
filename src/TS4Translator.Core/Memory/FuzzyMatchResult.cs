namespace TS4Translator.Core.Memory;

/// <param name="Entry">The closest previous translation found.</param>
/// <param name="Similarity">0.0-1.0 similarity ratio between the queried text and <see cref="Entry"/>'s original text.</param>
public sealed record FuzzyMatchResult(TranslationMemoryEntry Entry, double Similarity);
