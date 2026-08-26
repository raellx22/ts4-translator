using System.Globalization;

namespace TS4Translator.Core.Memory;

/// <param name="OriginalText">The exact source-language text this entry was translated from.</param>
/// <param name="SourceLocale">The source language.</param>
/// <param name="TargetLocale">The target language.</param>
/// <param name="TranslatedText">The stored translation.</param>
/// <param name="Provider">Which translation provider produced this (or "manual" for a human edit).</param>
/// <param name="ProfileName">The translation profile/style used, when known.</param>
/// <param name="CreatedAtUtc">When this entry was last written.</param>
public sealed record TranslationMemoryEntry(
    string OriginalText,
    CultureInfo SourceLocale,
    CultureInfo TargetLocale,
    string TranslatedText,
    string Provider,
    string? ProfileName,
    DateTimeOffset CreatedAtUtc);
