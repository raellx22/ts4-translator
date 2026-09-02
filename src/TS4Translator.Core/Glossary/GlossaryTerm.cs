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
    /// <summary>
    /// Whether this term shows up in <paramref name="text"/> as a whole word — used to select only the
    /// terms relevant to a batch rather than sending the whole glossary (brief section 23).
    /// </summary>
    /// <remarks>
    /// Word boundaries, not a plain substring search, and the difference stops being academic the moment
    /// the glossary holds ordinary words instead of only names. <c>Male → Masculino</c> is a perfectly
    /// good rule that a substring match fires on <b>Female</b>, instructing the engine to render
    /// "Female" as "Masculino"; <c>Hard</c> fires inside "Hardcore", <c>Idle</c> inside "Idles". Every
    /// one of those is a rule that makes the translation worse than having no glossary at all.
    ///
    /// A boundary here is "not a letter or digit", so a term still matches next to punctuation, brackets
    /// and markup — <c>(Male)</c>, <c>Male:</c>, <c>&lt;b&gt;Male&lt;/b&gt;</c> all count — while
    /// "Female" and "Malevolent" do not. Multi-word terms are matched the same way at each end.
    /// </remarks>
    public bool AppearsIn(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrEmpty(Original))
        {
            return false;
        }

        var comparison = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        for (var start = text.IndexOf(Original, comparison); start >= 0;
             start = text.IndexOf(Original, start + 1, comparison))
        {
            var beforeIsWord = start > 0 && IsWordCharacter(text[start - 1]);
            var end = start + Original.Length;
            var afterIsWord = end < text.Length && IsWordCharacter(text[end]);

            if (!beforeIsWord && !afterIsWord)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What counts as "inside a word". Underscores are deliberately word characters: a tuning identifier
    /// like <c>male_torso_ref</c> is code, not a label, and a glossary rule has no business firing in it.
    /// </summary>
    private static bool IsWordCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';
}
