using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TS4Translator.App.Services;
using TS4Translator.Core.Packages;
using TS4Translator.Core.Projects;

namespace TS4Translator.App.ViewModels;

/// <summary>
/// Backs the Home page: import `.package` files, create a project from them, and show recent
/// projects. Deliberately hardcodes English source / Portuguese (Brazil) target for now - a
/// locale picker belongs to a later, more complete import flow (brief section 34), not this first slice.
/// </summary>
public sealed partial class HomePageViewModel : ObservableObject
{
    private static readonly CultureInfo DefaultSourceLocale = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo DefaultTargetLocale = CultureInfo.GetCultureInfo("pt-BR");

    private readonly IProjectService _projectService;
    private readonly IPackageService _packageService;
    private readonly IPathPickerService _pathPicker;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    public HomePageViewModel(IProjectService projectService, IPackageService packageService, IPathPickerService pathPicker)
    {
        _projectService = projectService;
        _packageService = packageService;
        _pathPicker = pathPicker;
        StatusText = "Importe um ou mais arquivos .package para começar.";
        ImportPackagesCommand = new AsyncRelayCommand(ImportPackagesAsync);
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

    private async Task ImportPackagesAsync()
    {
        var files = await _pathPicker.PickPackageFilesAsync();
        if (files.Count == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var totalStringTables = 0;
            foreach (var file in files)
            {
                var keys = await _packageService.FindStringTablesAsync(file);
                totalStringTables += keys.Count;
            }

            var projectName = Path.GetFileNameWithoutExtension(files[0]);
            var project = await _projectService.CreateAsync(projectName, files, DefaultSourceLocale, DefaultTargetLocale);
            RecentProjects.Insert(0, project);

            StatusText = $"'{project.Name}' importado: {files.Count} arquivo(s), {totalStringTables} tabela(s) de strings encontrada(s).";
        }
        catch (Exception exception)
        {
            // Broad on purpose: LlamaLogic.Packages' exception surface for a corrupted/invalid
            // .package has not been characterized yet (brief section 27/46 - "package inválido").
            // Narrow this once that's known; for now, never let a bad file crash the import command.
            StatusText = $"Falha ao importar: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
