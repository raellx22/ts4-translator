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
    private readonly IStringTableService _stringTableService;
    private readonly IPathPickerService _pathPicker;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    public HomePageViewModel(
        IProjectService projectService,
        IProjectStringService projectStringService,
        IPackageService packageService,
        IStringTableService stringTableService,
        IPathPickerService pathPicker,
        GlossarySeeder glossarySeeder)
    {
        _projectService = projectService;
        _projectStringService = projectStringService;
        _packageService = packageService;
        _stringTableService = stringTableService;
        _pathPicker = pathPicker;
        StatusText = "Importe um ou mais arquivos .package para começar (ou arraste-os aqui).";
        ImportPackagesCommand = new AsyncRelayCommand(ImportViaPickerAsync);
        // A tela inicial e o unico ponto por onde todo mundo passa, e o seed precisa existir antes da
        // primeira traducao - nao so para quem abre a pagina de Glossario.
        _ = glossarySeeder.EnsureSeededAsync();
        _ = LoadRecentProjectsAsync();
    }

    public ObservableCollection<ProjectCardViewModel> RecentProjects { get; } = [];

    public IAsyncRelayCommand ImportPackagesCommand { get; }

    public async Task LoadRecentProjectsAsync()
    {
        var recent = await _projectService.ListRecentAsync();

        RecentProjects.Clear();
        foreach (var project in recent)
        {
            // Counted in SQL per project rather than by loading their strings - a single project can hold
            // six figures of rows, and this runs for every card on the screen.
            var progress = await _projectStringService.GetProgressAsync(project.Id);
            RecentProjects.Add(new ProjectCardViewModel(project, progress));
        }
    }

    /// <summary>
    /// Deletes a project and everything stored under it. The confirmation lives in the page, because
    /// this is irreversible: the strings, their revision history and any per-project engine settings go
    /// with it (all by database cascade). The imported `.package` files themselves are never touched.
    /// </summary>
    public async Task DeleteProjectAsync(ProjectCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);

        try
        {
            IsBusy = true;
            await _projectService.DeleteAsync(card.Id);
            await LoadRecentProjectsAsync();
            StatusText = $"Projeto \"{card.Name}\" apagado ({card.Progress.Total:N0} string(s) removidas).";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao apagar ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
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

            // Naming a twelve-mod project after its first file alone made the Home list look like only
            // that one mod had been imported.
            var projectName = files.Count == 1
                ? Path.GetFileNameWithoutExtension(files[0])
                : $"{Path.GetFileNameWithoutExtension(files[0])} + {files.Count - 1} outro(s)";
            // Re-importing the same mod must land in the project that already holds its translations.
            // Unconditionally calling CreateAsync produced a brand-new empty project every time, which
            // orphaned all previous work and made the new/changed/reused diff below always report
            // 100% "new" - the mod-update feature of brief section 25 never actually ran in the app.
            var project = await _projectService.FindBySourceFilesAsync(files);
            var isReimport = project is not null;
            project ??= await _projectService.CreateAsync(projectName, files, DefaultSourceLocale, DefaultTargetLocale);

            var importedTables = new List<ImportedTable>();
            var filesWithStrings = 0;
            var totalSkippedTables = 0;
            var totalNew = 0;
            var totalChanged = 0;
            var totalUnchanged = 0;
            var totalRemoved = 0;

            for (var index = 0; index < files.Count; index++)
            {
                var file = files[index];
                StatusText = $"Lendo {index + 1}/{files.Count}: {Path.GetFileName(file)}...";

                var keys = await _packageService.FindStringTablesAsync(file);
                var sourceKeys = SelectSourceLocaleTables(keys, project.SourceLocale);
                totalSkippedTables += keys.Count - sourceKeys.Count;

                var stringsFromThisFile = 0;
                foreach (var key in sourceKeys)
                {
                    var entries = await _packageService.ReadStringTableAsync(file, key);

                    // The source file is part of the table's identity, not a label: two of the twelve
                    // mods a user selects at once may ship an STBL with the same group and instance,
                    // and keying only on the STBL would make the second import erase the first mod's
                    // strings. It is also what lets the export write one package per mod.
                    var table = new ImportedTable(file, key);
                    var diff = await _projectStringService.ReplaceStringsAsync(project.Id, table, entries.Select(e => (e.KeyHash, e.Value)).ToList());
                    importedTables.Add(table);
                    stringsFromThisFile += diff.TotalAfterImport;
                    totalNew += diff.NewCount;
                    totalChanged += diff.ChangedCount;
                    totalUnchanged += diff.UnchangedCount;
                    totalRemoved += diff.RemovedCount;
                }

                if (stringsFromThisFile > 0)
                {
                    filesWithStrings++;
                }
            }

            // Guarded: an empty set would mean "delete this project's strings", and a package that
            // momentarily reads as having no STBL at all must never be able to wipe real work.
            var totalPruned = importedTables.Count > 0
                ? await _projectStringService.RemoveStringsOutsideAsync(project.Id, importedTables)
                : 0;

            await LoadRecentProjectsAsync();

            var totalStrings = totalNew + totalChanged + totalUnchanged;
            var skippedNote = totalSkippedTables > 0
                ? $" {totalSkippedTables} tabela(s) de outros idiomas do próprio mod foram ignoradas."
                : string.Empty;
            var prunedNote = totalPruned > 0
                ? $" {totalPruned} string(s) de tabelas que não fazem mais parte da importação foram descartadas."
                : string.Empty;

            // Reports how many of the selected files actually contributed strings, not just how many
            // were selected: importing twelve mods and getting one project named after the first was
            // indistinguishable from "only the first one worked".
            var emptyFiles = files.Count - filesWithStrings;
            var emptyNote = emptyFiles > 0
                ? $" {emptyFiles} arquivo(s) não tinham nenhuma string no idioma de origem."
                : string.Empty;

            StatusText = $"'{project.Name}' {(isReimport ? "reimportado" : "importado")}: {filesWithStrings} de {files.Count} arquivo(s) com strings, " +
                $"{importedTables.Count} tabela(s), {totalStrings} string(s) " +
                $"({totalNew} nova(s), {totalChanged} alterada(s), {totalUnchanged} reaproveitada(s){(totalRemoved > 0 ? $", {totalRemoved} removida(s)" : string.Empty)}).{emptyNote}{skippedNote}{prunedNote}";
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

    /// <summary>
    /// The STBLs of <paramref name="sourceLocale"/> only — the mod's actual source text.
    /// </summary>
    /// <remarks>
    /// A published mod ships one STBL per locale it already supports: the real
    /// `TURBODRIVER_WickedWhims_Tuning.package` has 110 tables, and only 5 of them are English.
    /// Importing all of them stored 149,138 strings instead of 6,779 — 22 copies of the same mod —
    /// so translation did 22x the work, fed the mod's own Russian/Chinese/Polish text to an
    /// English→Portuguese model, and made export impossible: retargeting 110 tables to pt-BR collapses
    /// them onto the same STBL keys, which the DBPF writer rejects as duplicates.
    ///
    /// Falls back to every table when the package has none in the source locale, so a mod that ships
    /// only, say, French strings still imports instead of silently producing an empty project.
    /// </remarks>
    private IReadOnlyList<StblKey> SelectSourceLocaleTables(IReadOnlyList<StblKey> keys, CultureInfo sourceLocale)
    {
        var matching = new List<StblKey>();
        foreach (var key in keys)
        {
            CultureInfo locale;
            try
            {
                locale = _stringTableService.GetLocale(key);
            }
            catch (Exception exception) when (exception is NotSupportedException or FormatException or ArgumentException)
            {
                // Top instance byte is not a locale The Sims 4 knows about - not source text.
                continue;
            }

            if (string.Equals(locale.Name, sourceLocale.Name, StringComparison.OrdinalIgnoreCase))
            {
                matching.Add(key);
            }
        }

        return matching.Count > 0 ? matching : keys;
    }
}
