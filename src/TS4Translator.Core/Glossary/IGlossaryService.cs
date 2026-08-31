namespace TS4Translator.Core.Glossary;

/// <summary>Manages glossary terms — global (apply to every project) and per-project (brief section 23).</summary>
public interface IGlossaryService
{
    Task<GlossaryTerm> AddTermAsync(string original, string translation, bool neverTranslate, bool caseSensitive, string? notes, Guid? projectId, CancellationToken cancellationToken = default);

    /// <summary>Global terms plus, when <paramref name="projectId"/> is given, that project's own terms.</summary>
    Task<IReadOnlyList<GlossaryTerm>> GetTermsAsync(Guid? projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Corrects an existing term in place. Without this the only way to fix a typo in a term was to
    /// delete it and retype it, and a glossary nobody can correct is a glossary nobody fills in.
    /// </summary>
    /// <returns>The updated term, or null if no term has that id.</returns>
    Task<GlossaryTerm?> UpdateTermAsync(int termId, string original, string translation, bool neverTranslate, bool caseSensitive, string? notes, CancellationToken cancellationToken = default);

    Task DeleteTermAsync(int termId, CancellationToken cancellationToken = default);
}
