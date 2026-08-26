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
    /// a brand-new hash is inserted as <see cref="ProjectStringStatus.Pending"/>.
    /// </summary>
    Task ReplaceStringsAsync(Guid projectId, StblKey sourceStbl, IReadOnlyList<(uint KeyHash, string Text)> entries, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectStringEntry>> GetStringsAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectStringEntry>> GetPendingStringsAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task UpdateTranslationAsync(int stringEntryId, string translatedText, ProjectStringStatus status, string? issueSummary, CancellationToken cancellationToken = default);
}
