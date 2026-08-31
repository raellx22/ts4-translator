namespace TS4Translator.Core.Projects;

/// <summary>A translation change persisted as part of a batch checkpoint.</summary>
public sealed record ProjectStringTranslationUpdate(
    int StringEntryId,
    string TranslatedText,
    ProjectStringStatus Status,
    string? IssueSummary);
