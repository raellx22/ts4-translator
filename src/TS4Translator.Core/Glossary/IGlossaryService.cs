namespace TS4Translator.Core.Glossary;

/// <summary>Manages glossary terms — global (apply to every project) and per-project (brief section 23).</summary>
public interface IGlossaryService
{
    Task<GlossaryTerm> AddTermAsync(string original, string translation, bool neverTranslate, bool caseSensitive, string? notes, Guid? projectId, CancellationToken cancellationToken = default);

    /// <summary>Global terms plus, when <paramref name="projectId"/> is given, that project's own terms.</summary>
    Task<IReadOnlyList<GlossaryTerm>> GetTermsAsync(Guid? projectId, CancellationToken cancellationToken = default);

    Task DeleteTermAsync(int termId, CancellationToken cancellationToken = default);
}
