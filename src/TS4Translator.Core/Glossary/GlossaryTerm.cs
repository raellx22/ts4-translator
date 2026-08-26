namespace TS4Translator.Core.Glossary;

/// <param name="Id">Persistence identity.</param>
/// <param name="Original">The source-language term.</param>
/// <param name="Translation">The required translation. Ignored (but still respected as "do not translate") when <paramref name="NeverTranslate"/> is true.</param>
/// <param name="NeverTranslate">When true, the term must be left exactly as-is (e.g. mod/creator names like "WickedWhims").</param>
/// <param name="CaseSensitive">Whether matching against source text should respect case.</param>
/// <param name="Notes">Free-form context for a human reviewer.</param>
/// <param name="ProjectId">Null for a global term (applies to every project); set for a project-specific term.</param>
public sealed record GlossaryTerm(int Id, string Original, string Translation, bool NeverTranslate, bool CaseSensitive, string? Notes, Guid? ProjectId)
{
    /// <summary>Whether this term shows up in <paramref name="text"/> — used to select only relevant terms for a batch rather than sending the whole glossary (brief section 23).</summary>
    public bool AppearsIn(string text) =>
        text.Contains(Original, CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
