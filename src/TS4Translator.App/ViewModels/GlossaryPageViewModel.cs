using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TS4Translator.Core.Glossary;

namespace TS4Translator.App.ViewModels;

/// <summary>
/// Backs the Glossary page (brief section 23). Manages the global glossary - terms that apply to
/// every project. Per-project terms exist in <see cref="IGlossaryService"/> already
/// (<c>AddTermAsync</c> takes an optional project id) but have no dedicated UI yet; this page only
/// covers the global scope, which is the common case (character/place names, brand terms).
/// </summary>
public sealed partial class GlossaryPageViewModel(IGlossaryService glossaryService) : ObservableObject
{
    public ObservableCollection<GlossaryTerm> Terms { get; } = [];

    [ObservableProperty]
    public partial string NewOriginal { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewTranslation { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool NewNeverTranslate { get; set; }

    [ObservableProperty]
    public partial bool NewCaseSensitive { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        var terms = await glossaryService.GetTermsAsync(projectId: null);
        Terms.Clear();
        foreach (var term in terms)
        {
            Terms.Add(term);
        }
    }

    [RelayCommand]
    private async Task AddTermAsync()
    {
        try
        {
            IsBusy = true;

            if (string.IsNullOrWhiteSpace(NewOriginal))
            {
                StatusText = "O texto original é obrigatório.";
                return;
            }

            if (!NewNeverTranslate && string.IsNullOrWhiteSpace(NewTranslation))
            {
                StatusText = "A tradução é obrigatória, a menos que \"Nunca traduzir\" esteja marcado.";
                return;
            }

            var term = await glossaryService.AddTermAsync(
                NewOriginal, NewNeverTranslate ? NewOriginal : NewTranslation, NewNeverTranslate, NewCaseSensitive,
                notes: null, projectId: null);

            Terms.Add(term);
            NewOriginal = string.Empty;
            NewTranslation = string.Empty;
            NewNeverTranslate = false;
            NewCaseSensitive = false;
            StatusText = $"Termo \"{term.Original}\" adicionado.";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao adicionar ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteTermAsync(GlossaryTerm term)
    {
        try
        {
            await glossaryService.DeleteTermAsync(term.Id);
            Terms.Remove(term);
            StatusText = $"Termo \"{term.Original}\" removido.";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao remover ({exception.GetType().Name}): {exception.Message}";
        }
    }
}
