using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TS4Translator.App.Services;
using TS4Translator.Core.Packages;
using TS4Translator.Core.Projects;

namespace TS4Translator.App.ViewModels;

/// <summary>
/// Backs the Home page: import `.package` files (via picker or drag-and-drop, brief section 34),
/// create a project from them, extract every STBL's strings into it, and show recent projects.
/// Deliberately hardcodes English source / Portuguese (Brazil) target for now - a locale picker
/// belongs to a later, more complete import flow, not this first slice.
/// </summary>
public sealed partial class HomePageViewModel : ObservableObject
{
    private static readonly CultureInfo DefaultSourceLocale = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo DefaultTargetLocale = CultureInfo.GetCultureInfo("pt-BR");

    private readonly IProjectService _projectService;
    private readonly IProjectStringService _projectStringService;
    private readonly IPackageService _packageService;
    private readonly IPathPickerService _pathPicker;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    public HomePageViewModel(
        IProjectService projectService,
        IProjectStringService projectStringService,
        IPackageService packageService,
        IPathPickerService pathPicker)
    {
        _projectService = projectService;
        _projectStringService = projectStringService;
        _packageService = packageService;
        _pathPicker = pathPicker;
        StatusText = "Importe um ou mais arquivos .package para começar (ou arraste-os aqui).";
        ImportPackagesCommand = new AsyncRelayCommand(ImportViaPickerAsync);
        _ = LoadRecentProjectsAsync();
    }

    public ObservableCollection<TranslationProject> RecentProjects { get; } = [];

    public IAsyncRelayCommand ImportPackagesCommand { get; }

    public async Task LoadRecentProjectsAsync()
    {
        var recent = await _projectService.ListRecentAsync();
        RecentProjects.Clear();
        foreach (var project in recent)
        {
            RecentProjects.Add(project);
        }
    }

    private Task ImportViaPickerAsync() => RunImportAsync(async () =>
    {
        var files = await _pathPicker.PickPackageFilesAsync();
        return files;
    });

    /// <summary>Entry point for drag-and-drop (brief section 34) - the code-behind pre-filters to `.package` files before calling this.</summary>
    public Task ImportFilesAsync(IReadOnlyList<string> files) => RunImportAsync(() => Task.FromResult(files));

    private async Task RunImportAsync(Func<Task<IReadOnlyList<string>>> resolveFiles)
    {
        // Everything in this method - including any picker call - must stay inside the try/catch.
        // AsyncRelayCommand does not rethrow exceptions from its delegate onto the UI thread by
        // default: an exception here would otherwise be silently swallowed (the command would just
        // appear to do nothing). Confirmed by actually running the app - the picker call originally
        // sat outside this try/catch and failures were invisible.
        try
        {
            var files = await resolveFiles();
            if (files.Count == 0)
            {
                return;
            }

            IsBusy = true;

            var projectName = Path.GetFileNameWithoutExtension(files[0]);
            var project = await _projectService.CreateAsync(projectName, files, DefaultSourceLocale, DefaultTargetLocale);

            var totalStringTables = 0;
            var totalNew = 0;
            var totalChanged = 0;
            var totalUnchanged = 0;
            var totalRemoved = 0;

            foreach (var file in files)
            {
                var keys = await _packageService.FindStringTablesAsync(file);
                foreach (var key in keys)
                {
                    var entries = await _packageService.ReadStringTableAsync(file, key);
                    var diff = await _projectStringService.ReplaceStringsAsync(project.Id, key, entries.Select(e => (e.KeyHash, e.Value)).ToList());
                    totalStringTables++;
                    totalNew += diff.NewCount;
                    totalChanged += diff.ChangedCount;
                    totalUnchanged += diff.UnchangedCount;
                    totalRemoved += diff.RemovedCount;
                }
            }

            RecentProjects.Insert(0, project);
            var totalStrings = totalNew + totalChanged + totalUnchanged;
            StatusText = $"'{project.Name}' importado: {files.Count} arquivo(s), {totalStringTables} tabela(s), {totalStrings} string(s) " +
                $"({totalNew} nova(s), {totalChanged} alterada(s), {totalUnchanged} reaproveitada(s){(totalRemoved > 0 ? $", {totalRemoved} removida(s)" : string.Empty)}).";
        }
        catch (Exception exception)
        {
            // Broad on purpose: LlamaLogic.Packages' exception surface for a corrupted/invalid
            // .package has not been characterized yet (brief section 27/46 - "package inválido").
            // Narrow this once that's known; for now, never let a bad file crash the import command.
            StatusText = $"Falha ao importar ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
