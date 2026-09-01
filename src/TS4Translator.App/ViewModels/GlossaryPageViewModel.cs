using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TS4Translator.App.Services;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Projects;

namespace TS4Translator.App.ViewModels;

/// <summary>
/// Backs the Glossary page (brief section 23): global terms, and the terms of one project at a time.
/// </summary>
/// <remarks>
/// The glossary reaches every engine the app can drive — including, now, a chat window, via the exported
/// bundle — and the import checks the reply against it. That makes a term entered here the cheapest
/// correction in the app: one line, applied to every mod, on every engine, from then on.
///
/// Per-project terms had been supported by the service since the beginning and had no interface, so in
/// practice they did not exist. They matter for exactly the case a global glossary handles badly: a word
/// one mod uses in its own particular sense, where forcing that reading on every other mod would be
/// wrong.
/// </remarks>
public sealed partial class GlossaryPageViewModel(
    IGlossaryService glossaryService,
    IProjectService projectService,
    GlossarySeeder glossarySeeder) : ObservableObject
{
    /// <summary>
    /// Puts back the names the app ships with, for a default removed by accident.
    /// </summary>
    /// <remarks>
    /// Explicit, and never automatic: removing a default is a decision, and re-adding it on the next
    /// launch would quietly overrule the user. Only terms that are actually missing are added, so this
    /// never duplicates or resets an edited one.
    /// </remarks>
    [RelayCommand]
    private async Task RestoreDefaultsAsync()
    {
        try
        {
            IsBusy = true;

            var added = await glossarySeeder.RestoreAsync();
            await ReloadTermsAsync();

            StatusText = added == 0
                ? "Os termos padrão já estão todos aqui."
                : $"{added} termo(s) padrão restaurado(s).";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao restaurar ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>The scope entry that means "terms that apply everywhere".</summary>
    public const string GlobalScope = "Todos os projetos (global)";

    public ObservableCollection<GlossaryTermViewModel> Terms { get; } = [];

    public ObservableCollection<string> ScopeOptions { get; } = [GlobalScope];

    /// <summary>
    /// Which glossary is being shown and where a new term will be created. Picking a project shows its
    /// own terms alongside the global ones, because that is the set that project actually translates
    /// with — seeing only half of it would be misleading.
    /// </summary>
    [ObservableProperty]
    public partial string SelectedScope { get; set; } = GlobalScope;

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

    private readonly Dictionary<string, Guid> _projectIdsByName = [];

    /// <summary>Null when the global scope is selected.</summary>
    private Guid? SelectedProjectId =>
        _projectIdsByName.TryGetValue(SelectedScope, out var id) ? id : null;

    partial void OnSelectedScopeChanged(string value) => _ = ReloadTermsAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        var projects = await projectService.ListRecentAsync(int.MaxValue);

        _projectIdsByName.Clear();
        ScopeOptions.Clear();
        ScopeOptions.Add(GlobalScope);

        foreach (var project in projects)
        {
            // Two projects can share a name; the id decides, so a duplicate name is disambiguated rather
            // than silently pointing both entries at whichever was added first.
            var label = _projectIdsByName.ContainsKey(project.Name) ? $"{project.Name} ({project.Id.ToString()[..8]})" : project.Name;
            _projectIdsByName[label] = project.Id;
            ScopeOptions.Add(label);
        }

        SelectedScope = GlobalScope;
        await ReloadTermsAsync();
    }

    private async Task ReloadTermsAsync()
    {
        try
        {
            IsBusy = true;

            var projectId = SelectedProjectId;
            var terms = await glossaryService.GetTermsAsync(projectId);

            Terms.Clear();
            foreach (var term in terms)
            {
                Terms.Add(new GlossaryTermViewModel(term, term.ProjectId is null ? "Global" : SelectedScope));
            }

            StatusText = Describe(terms, projectId);
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao carregar ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Describe(IReadOnlyList<GlossaryTerm> terms, Guid? projectId)
    {
        if (terms.Count == 0)
        {
            return "Nenhum termo ainda. Um termo aqui vale para todos os motores, inclusive para o arquivo que você leva a um chat de IA - e a volta é conferida contra ele.";
        }

        if (projectId is null)
        {
            return $"{terms.Count} termo(s) globais.";
        }

        var own = terms.Count(term => term.ProjectId is not null);
        return $"{terms.Count} termo(s) valendo neste projeto - {own} só dele, {terms.Count - own} globais.";
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

            var projectId = SelectedProjectId;
            var term = await glossaryService.AddTermAsync(
                NewOriginal, NewNeverTranslate ? NewOriginal : NewTranslation, NewNeverTranslate, NewCaseSensitive,
                notes: null, projectId: projectId);

            Terms.Add(new GlossaryTermViewModel(term, projectId is null ? "Global" : SelectedScope));
            NewOriginal = string.Empty;
            NewTranslation = string.Empty;
            NewNeverTranslate = false;
            NewCaseSensitive = false;
            StatusText = projectId is null
                ? $"Termo \"{term.Original}\" adicionado ao glossário global."
                : $"Termo \"{term.Original}\" adicionado só a \"{SelectedScope}\".";
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

    /// <summary>Persists an in-place correction to an existing term.</summary>
    public async Task SaveTermAsync(GlossaryTermViewModel term)
    {
        ArgumentNullException.ThrowIfNull(term);

        if (!term.HasUnsavedEdit)
        {
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(term.Original))
            {
                StatusText = "O texto original é obrigatório.";
                term.RevertEdit();
                return;
            }

            // A "never translate" term uses its own source text as the translation, so the two can never
            // drift apart and the export has something concrete to print either way.
            var translation = term.NeverTranslate ? term.Original : term.Translation;
            if (string.IsNullOrWhiteSpace(translation))
            {
                StatusText = "A tradução é obrigatória, a menos que \"Nunca traduzir\" esteja marcado.";
                term.RevertEdit();
                return;
            }

            var saved = await glossaryService.UpdateTermAsync(
                term.Id, term.Original, translation, term.NeverTranslate, term.CaseSensitive, notes: null);

            if (saved is null)
            {
                StatusText = "Esse termo não existe mais. Recarregue a página.";
                return;
            }

            term.MarkSaved(saved);
            StatusText = $"Termo \"{saved.Original}\" atualizado.";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao salvar ({exception.GetType().Name}): {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteTermAsync(GlossaryTermViewModel term)
    {
        ArgumentNullException.ThrowIfNull(term);

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
