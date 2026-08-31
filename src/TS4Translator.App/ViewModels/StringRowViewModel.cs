using CommunityToolkit.Mvvm.ComponentModel;
using TS4Translator.Core.Projects;

namespace TS4Translator.App.ViewModels;

/// <summary>Display wrapper around a <see cref="ProjectStringEntry"/> for the editor's virtualized list.</summary>
public sealed partial class StringRowViewModel(ProjectStringEntry entry) : ObservableObject
{
    public int Id { get; } = entry.Id;

    public string Original { get; } = entry.OriginalText;

    /// <summary>Which imported mod this string came from — shown so a multi-mod project is legible.</summary>
    public string? SourceModName { get; } = entry.SourceFile is null ? null : Path.GetFileNameWithoutExtension(entry.SourceFile);

    public string? SourceFile { get; } = entry.SourceFile;

    [ObservableProperty]
    public partial string? Translation { get; set; } = entry.TranslatedText;

    [ObservableProperty]
    public partial string StatusText { get; set; } = Describe(entry.Status, entry.IssueSummary);

    /// <summary>True while this specific row is being refined, so its row can show a spinner.</summary>
    [ObservableProperty]
    public partial bool IsRefining { get; set; }

    /// <summary>Ticked by the user to include this string in a chat bundle export.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>
    /// Called when this row is ticked or unticked, so the editor can restate how big the next export
    /// would be. A plain delegate rather than an event subscription: a project holds six figures of rows,
    /// and the editor is the only listener there will ever be.
    /// </summary>
    public Action? SelectionChanged { get; set; }

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke();

    public ProjectStringStatus Status { get; private set; } = entry.Status;

    /// <summary>
    /// Whether this string has ever come back flagged. Feeds StringRiskContext: shape alone cannot see
    /// that "Shallow" was mistranslated, but the app remembers that it was.
    /// </summary>
    public bool WasEverFlaggedForReview { get; private set; } = entry.Status == ProjectStringStatus.NeedsReview;

    /// <summary>Whether refining this row is worth offering — there has to be a translation to improve.</summary>
    public bool CanRefine => !string.IsNullOrWhiteSpace(Translation);

    /// <summary>
    /// The translation as it is currently stored. The editor's translation box saves on LostFocus, so
    /// without this every click into a box and back out again would be persisted as a manual edit and
    /// flip a perfectly good machine translation to "Editado manualmente" — losing the real status and
    /// writing a pointless revision. Compared against before saving; see <see cref="HasUnsavedEdit"/>.
    /// </summary>
    private string? _persistedTranslation = entry.TranslatedText;

    public bool HasUnsavedEdit => !string.Equals(Translation ?? string.Empty, _persistedTranslation ?? string.Empty, StringComparison.Ordinal);

    public void ApplyResult(string? translation, ProjectStringStatus status, string? issueSummary)
    {
        Translation = translation;
        _persistedTranslation = translation;
        Status = status;
        WasEverFlaggedForReview |= status == ProjectStringStatus.NeedsReview;
        StatusText = Describe(status, issueSummary);
        OnPropertyChanged(nameof(CanRefine));
    }

    private static string Describe(ProjectStringStatus status, string? issueSummary) => status switch
    {
        ProjectStringStatus.Pending => "Pendente",
        ProjectStringStatus.Translated => "Traduzido",
        ProjectStringStatus.NeedsReview => $"Revisar: {issueSummary}",
        ProjectStringStatus.ManuallyEdited => "Editado manualmente",
        ProjectStringStatus.Refined => "Refinado",
        _ => status.ToString(),
    };
}
