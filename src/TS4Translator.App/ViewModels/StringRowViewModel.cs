using CommunityToolkit.Mvvm.ComponentModel;
using TS4Translator.Core.Projects;

namespace TS4Translator.App.ViewModels;

/// <summary>Display wrapper around a <see cref="ProjectStringEntry"/> for the editor's virtualized list.</summary>
public sealed partial class StringRowViewModel(ProjectStringEntry entry) : ObservableObject
{
    public int Id { get; } = entry.Id;

    public string Original { get; } = entry.OriginalText;

    [ObservableProperty]
    public partial string? Translation { get; set; } = entry.TranslatedText;

    [ObservableProperty]
    public partial string StatusText { get; set; } = Describe(entry.Status, entry.IssueSummary);

    public ProjectStringStatus Status { get; private set; } = entry.Status;

    public void ApplyResult(string? translation, ProjectStringStatus status, string? issueSummary)
    {
        Translation = translation;
        Status = status;
        StatusText = Describe(status, issueSummary);
    }

    private static string Describe(ProjectStringStatus status, string? issueSummary) => status switch
    {
        ProjectStringStatus.Pending => "Pendente",
        ProjectStringStatus.Translated => "Traduzido",
        ProjectStringStatus.NeedsReview => $"Revisar: {issueSummary}",
        ProjectStringStatus.ManuallyEdited => "Editado manualmente",
        _ => status.ToString(),
    };
}
