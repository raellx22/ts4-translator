using System.Globalization;

namespace TS4Translator.Core.Projects;

/// <summary>
/// Persists and retrieves translation projects — the unit the UI's "recent projects" screen and the
/// mod-update engine (brief sections 25/33) are built around. A project ties a set of imported
/// `.package` files to a locale pair; it does not itself hold string entries or translations
/// (those live in translation memory and, later, per-project string tables).
/// </summary>
public interface IProjectService
{
    Task<TranslationProject> CreateAsync(
        string name,
        IReadOnlyList<string> sourcePackagePaths,
        CultureInfo sourceLocale,
        CultureInfo targetLocale,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The existing project whose imported `.package` set is exactly <paramref name="sourcePackagePaths"/>,
    /// or <c>null</c> when this is a first import. Re-importing a mod has to land in the project that
    /// already holds its translations — creating a second one orphans all of that work and makes the
    /// new/changed/reused diff of brief section 25 meaningless (it would always report 100% "new").
    /// </summary>
    Task<TranslationProject?> FindBySourceFilesAsync(IReadOnlyList<string> sourcePackagePaths, CancellationToken cancellationToken = default);

    /// <summary>Most recently updated projects first.</summary>
    Task<IReadOnlyList<TranslationProject>> ListRecentAsync(int maxCount = 20, CancellationToken cancellationToken = default);

    Task<TranslationProject?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Sets or clears the project's custom translation instructions (brief section 22).</summary>
    Task UpdateCustomInstructionsAsync(Guid id, string? customInstructions, CancellationToken cancellationToken = default);
}
