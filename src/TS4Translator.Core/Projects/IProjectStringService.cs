using TS4Translator.Core.Packages;

namespace TS4Translator.Core.Projects;

/// <summary>
/// Persists and retrieves the individual strings extracted from a project's source packages.
/// </summary>
public interface IProjectStringService
{
    /// <summary>
    /// Replaces every stored string for <paramref name="sourceStbl"/> within <paramref name="projectId"/>
    /// with <paramref name="entries"/>. Used on (re-)import: an entry whose <c>KeyHash</c> already
    /// exists keeps its current translation/status (brief section 25 - don't discard work on re-import);
    /// a brand-new hash is inserted as <see cref="ProjectStringStatus.Pending"/>. A hash whose text
    /// changed keeps the old translation but its status becomes <see cref="ProjectStringStatus.NeedsReview"/>,
    /// so a stale translation is never silently presented as current.
    /// </summary>
    Task<ImportDiffResult> ReplaceStringsAsync(Guid projectId, StblKey sourceStbl, IReadOnlyList<(uint KeyHash, string Text)> entries, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectStringEntry>> GetStringsAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectStringEntry>> GetPendingStringsAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Updates a string's translation. If it previously had a different non-null translation, that value is kept as a recoverable revision (brief section 31).</summary>
    Task UpdateTranslationAsync(int stringEntryId, string translatedText, ProjectStringStatus status, string? issueSummary, CancellationToken cancellationToken = default);

    /// <summary>Previous translations for this string, most recent first.</summary>
    Task<IReadOnlyList<StringRevision>> GetHistoryAsync(int stringEntryId, CancellationToken cancellationToken = default);
}
