using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TS4Translator.App.Services;
using TS4Translator.Core.Packages;
using TS4Translator.Core.Projects;
using TS4Translator.Core.Translation;

namespace TS4Translator.App.ViewModels;

/// <summary>
/// Backs the Editor page: shows a project's extracted strings, translates the pending ones through
/// the configured provider, and exports a translated package with round-trip validation before
/// reporting success (brief section 28).
/// </summary>
public sealed partial class EditorPageViewModel(
    IProjectService projectService,
    IProjectStringService projectStringService,
    ITranslationOrchestrator translationOrchestrator,
    ITranslationProviderFactory providerFactory,
    IPackageService packageService,
    IStringTableService stringTableService,
    IPathPickerService pathPicker) : ObservableObject
{
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProjectName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedFilter { get; set; } = AllFilter;

    private const string AllFilter = "Todas";

    private TranslationProject? _project;
    private readonly List<StringRowViewModel> _allRows = [];

    public IReadOnlyList<string> FilterOptions { get; } = [AllFilter, "Pendentes", "Traduzidas", "Revisar", "Editadas manualmente"];

    public ObservableCollection<StringRowViewModel> Strings { get; } = [];

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        IEnumerable<StringRowViewModel> filtered = _allRows;

        filtered = SelectedFilter switch
        {
            "Pendentes" => filtered.Where(r => r.Status == ProjectStringStatus.Pending),
            "Traduzidas" => filtered.Where(r => r.Status == ProjectStringStatus.Translated),
            "Revisar" => filtered.Where(r => r.Status == ProjectStringStatus.NeedsReview),
            "Editadas manualmente" => filtered.Where(r => r.Status == ProjectStringStatus.ManuallyEdited),
            _ => filtered,
        };

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(r =>
                r.Original.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                (r.Translation?.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ?? false));
        }

        Strings.Clear();
        foreach (var row in filtered)
        {
            Strings.Add(row);
        }
    }

    [RelayCommand]
    private async Task LoadAsync(Guid projectId)
    {
        _project = await projectService.GetAsync(projectId);
        if (_project is null)
        {
            StatusText = "Projeto não encontrado.";
            return;
        }

        ProjectName = _project.Name;
        StatusText = $"{_project.SourceLocale.Name} → {_project.TargetLocale.Name}";

        var entries = await projectStringService.GetStringsAsync(projectId);
        _allRows.Clear();
        _allRows.AddRange(entries.Select(e => new StringRowViewModel(e)));
        ApplyFilter();
    }

    /// <summary>Persists a manual edit made directly in the editor (brief sections 30/31). The previous translation, if any, is kept as a recoverable revision.</summary>
    public async Task SaveManualEditAsync(StringRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        try
        {
            var newText = row.Translation ?? string.Empty;
            await projectStringService.UpdateTranslationAsync(row.Id, newText, ProjectStringStatus.ManuallyEdited, issueSummary: null);
            row.ApplyResult(newText, ProjectStringStatus.ManuallyEdited, issueSummary: null);
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao salvar edição ({exception.GetType().Name}): {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task TranslateAsync()
    {
        if (_project is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = "Verificando provider configurado...";

            var provider = await providerFactory.CreateActiveProviderAsync();
            if (provider is null)
            {
                StatusText = "Nenhum provider configurado. Vá em Configurações e configure um endpoint compatível com OpenAI (ex.: LM Studio ou NVIDIA NIM).";
                return;
            }

            var pending = await projectStringService.GetPendingStringsAsync(_project.Id);
            if (pending.Count == 0)
            {
                StatusText = "Nenhuma string pendente de tradução.";
                return;
            }

            StatusText = $"Traduzindo {pending.Count} string(s) via {provider.Name}...";

            var items = pending
                .Select(e => new TranslationSourceItem(e.Id.ToString(CultureInfo.InvariantCulture), e.OriginalText))
                .ToList();

            var outcomes = await translationOrchestrator.TranslateAsync(items, provider, _project.SourceLocale, _project.TargetLocale);

            var succeeded = 0;
            var needsReview = 0;
            var failed = 0;

            foreach (var outcome in outcomes)
            {
                var stringId = int.Parse(outcome.Id, CultureInfo.InvariantCulture);

                if (outcome.TranslatedText is null)
                {
                    failed++;
                    continue;
                }

                var status = outcome.Success ? ProjectStringStatus.Translated : ProjectStringStatus.NeedsReview;
                var issueSummary = outcome.Issues.Count > 0 ? outcome.Issues[0].Message : null;

                await projectStringService.UpdateTranslationAsync(stringId, outcome.TranslatedText, status, issueSummary);

                var row = _allRows.FirstOrDefault(r => r.Id == stringId);
                row?.ApplyResult(outcome.TranslatedText, status, issueSummary);

                if (outcome.Success)
                {
                    succeeded++;
                }
                else
                {
                    needsReview++;
                }
            }

            ApplyFilter();
            StatusText = $"Tradução concluída: {succeeded} traduzida(s), {needsReview} para revisar, {failed} sem resposta do provider.";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao traduzir ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (_project is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = "Escolhendo local de saída...";

            var suggestedName = $"{_project.Name}_{_project.TargetLocale.Name}.package";
            var outputPath = await pathPicker.PickSaveFileAsync(suggestedName);
            if (outputPath is null)
            {
                return;
            }

            var entries = await projectStringService.GetStringsAsync(_project.Id);
            var tables = entries
                .GroupBy(e => e.SourceStbl)
                .ToDictionary(
                    g => stringTableService.WithLocale(g.Key, _project.TargetLocale),
                    IReadOnlyList<StringTableEntry> (g) => g.Select(e => new StringTableEntry(e.KeyHash, e.TranslatedText ?? e.OriginalText)).ToList());

            // FileSavePicker.PickSaveFileAsync() creates an empty 0-byte file the moment the user
            // confirms a name/location, before we ever write to it. WriteStringTablesAsync refuses
            // to overwrite an existing file (never silently clobber a package) - remove that stub
            // first so the check doesn't misfire against the picker's own placeholder.
            if (File.Exists(outputPath) && new FileInfo(outputPath).Length == 0)
            {
                File.Delete(outputPath);
            }

            StatusText = "Gravando package traduzido...";
            await packageService.WriteStringTablesAsync(outputPath, tables);

            StatusText = "Validando o package gerado (reabrindo e conferindo cada string)...";
            var isValid = await ValidateExportAsync(outputPath, tables);

            StatusText = isValid
                ? $"Exportado e validado com sucesso: {Path.GetFileName(outputPath)} ({tables.Count} tabela(s), {tables.Sum(t => t.Value.Count)} string(s))."
                : $"Exportado, mas a validação de round-trip encontrou diferenças em {Path.GetFileName(outputPath)}. Não confie neste arquivo sem investigar.";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao exportar ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> ValidateExportAsync(string outputPath, Dictionary<StblKey, IReadOnlyList<StringTableEntry>> expectedTables)
    {
        var foundKeys = await packageService.FindStringTablesAsync(outputPath);
        if (foundKeys.Count != expectedTables.Count)
        {
            return false;
        }

        foreach (var (key, expectedEntries) in expectedTables)
        {
            if (!foundKeys.Contains(key))
            {
                return false;
            }

            var actualEntries = await packageService.ReadStringTableAsync(outputPath, key);
            if (actualEntries.Count != expectedEntries.Count)
            {
                return false;
            }

            var actualByHash = actualEntries.ToDictionary(e => e.KeyHash, e => e.Value);
            foreach (var expected in expectedEntries)
            {
                if (!actualByHash.TryGetValue(expected.KeyHash, out var actualValue) || actualValue != expected.Value)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
