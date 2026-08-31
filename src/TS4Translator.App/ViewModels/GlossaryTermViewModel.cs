using CommunityToolkit.Mvvm.ComponentModel;
using TS4Translator.Core.Glossary;

namespace TS4Translator.App.ViewModels;

/// <summary>
/// An editable row of the glossary. Terms were previously add-or-delete only, which meant a typo in a
/// term had to be retyped from scratch — a small friction on the one feature that only pays off if the
/// user keeps it tidy.
/// </summary>
public sealed partial class GlossaryTermViewModel(GlossaryTerm term, string scopeText) : ObservableObject
{
    public int Id { get; } = term.Id;

    /// <summary>
    /// "Global" or the project's name. Shown because a project's list mixes both, and a term that looks
    /// local but is global is a trap: correcting it there changes every other mod too.
    /// </summary>
    public string ScopeText { get; } = scopeText;

    [ObservableProperty]
    public partial string Original { get; set; } = term.Original;

    [ObservableProperty]
    public partial string Translation { get; set; } = term.Translation;

    [ObservableProperty]
    public partial bool NeverTranslate { get; set; } = term.NeverTranslate;

    [ObservableProperty]
    public partial bool CaseSensitive { get; set; } = term.CaseSensitive;

    private GlossaryTerm _persisted = term;

    public bool HasUnsavedEdit =>
        !string.Equals(Original, _persisted.Original, StringComparison.Ordinal)
        || !string.Equals(Translation, _persisted.Translation, StringComparison.Ordinal)
        || NeverTranslate != _persisted.NeverTranslate
        || CaseSensitive != _persisted.CaseSensitive;

    public void MarkSaved(GlossaryTerm saved)
    {
        _persisted = saved;
        Original = saved.Original;
        Translation = saved.Translation;
        NeverTranslate = saved.NeverTranslate;
        CaseSensitive = saved.CaseSensitive;
    }

    public void RevertEdit() => MarkSaved(_persisted);
}
