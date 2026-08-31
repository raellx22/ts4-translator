using TS4Translator.Core.Packages;

namespace TS4Translator.Core.Projects;

/// <summary>
/// Persists and retrieves the individual strings extracted from a project's source packages.
/// </summary>
public interface IProjectStringService
{
    /// <summary>
    /// Replaces every stored string for <paramref name="table"/> within <paramref name="projectId"/>
    /// with <paramref name="entries"/>. Used on (re-)import: an entry whose <c>KeyHash</c> already
    /// exists keeps its current translation/status (brief section 25 - don't discard work on re-import);
    /// a brand-new hash is inserted as <see cref="ProjectStringStatus.Pending"/>. A hash whose text
    /// changed keeps the old translation but its status becomes <see cref="ProjectStringStatus.NeedsReview"/>,
    /// so a stale translation is never silently presented as current.
    /// </summary>
    Task<ImportDiffResult> ReplaceStringsAsync(Guid projectId, ImportedTable table, IReadOnlyList<(uint KeyHash, string Text)> entries, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every stored string of <paramref name="projectId"/> whose table is not in
    /// <paramref name="importedTables"/>, and returns how many rows were removed.
    /// <see cref="ReplaceStringsAsync"/> only ever prunes *within* a table it is given, so a table that
    /// stopped being imported altogether would otherwise linger forever and end up in the export.
    /// Callers must pass every table the import actually read, and must not call this with an empty
    /// set unless they really mean "this project has no strings".
    /// </summary>
    Task<int> RemoveStringsOutsideAsync(Guid projectId, IReadOnlyCollection<ImportedTable> importedTables, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectStringEntry>> GetStringsAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts by status for one project, without loading its strings. The Home list needs this for every
    /// project it shows, and a project can hold six figures of rows - materialising them just to count
    /// would make opening the app crawl.
    /// </summary>
    Task<ProjectProgress> GetProgressAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectStringEntry>> GetPendingStringsAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Updates a string's translation. If it previously had a different non-null translation, that value is kept as a recoverable revision (brief section 31).</summary>
    Task UpdateTranslationAsync(int stringEntryId, string translatedText, ProjectStringStatus status, string? issueSummary, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists one translation checkpoint in a single batch. Implementations should keep the same
    /// revision guarantees as <see cref="UpdateTranslationAsync"/>.
    /// </summary>
    async Task UpdateTranslationsAsync(IReadOnlyList<ProjectStringTranslationUpdate> updates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);
        foreach (var update in updates)
        {
            await UpdateTranslationAsync(update.StringEntryId, update.TranslatedText, update.Status, update.IssueSummary, cancellationToken);
        }
    }

    /// <summary>Previous translations for this string, most recent first.</summary>
    Task<IReadOnlyList<StringRevision>> GetHistoryAsync(int stringEntryId, CancellationToken cancellationToken = default);
}
