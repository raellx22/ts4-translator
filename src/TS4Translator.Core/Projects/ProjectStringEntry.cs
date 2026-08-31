using TS4Translator.Core.Packages;

namespace TS4Translator.Core.Projects;

/// <param name="Id">Persistence identity for this row.</param>
/// <param name="ProjectId">The project this string belongs to.</param>
/// <param name="SourceFile">Which imported `.package` this string came from — null for rows stored before the app tracked it. Lets the export write one package per mod.</param>
/// <param name="SourceStbl">Which STBL resource (and therefore which group/instance) this string came from.</param>
/// <param name="KeyHash">The STBL entry's stable key hash — never changes across a translation, per brief section 8.</param>
/// <param name="OriginalText">The source-language text, exactly as extracted.</param>
/// <param name="TranslatedText">The current translation, or null if not yet translated.</param>
/// <param name="Status">Where this string is in the translation workflow.</param>
/// <param name="IssueSummary">A short human-readable summary of the worst validation issue found, or null.</param>
public sealed record ProjectStringEntry(
    int Id,
    Guid ProjectId,
    string? SourceFile,
    StblKey SourceStbl,
    uint KeyHash,
    string OriginalText,
    string? TranslatedText,
    ProjectStringStatus Status,
    string? IssueSummary);
