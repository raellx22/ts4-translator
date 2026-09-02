using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TS4Translator.App.Services;
using TS4Translator.Core.Glossary;
using TS4Translator.Core.Interchange;
using TS4Translator.Core.Memory;
using TS4Translator.Core.Packages;
using TS4Translator.Core.Projects;
using TS4Translator.Core.Tokens;
using TS4Translator.Core.Providers;
using TS4Translator.Core.Translation;
using TS4Translator.Core.Validation;

namespace TS4Translator.App.ViewModels;

/// <summary>
/// Backs the Editor page: shows a project's extracted strings, translates the pending ones through the
/// profiles assigned in Settings (optionally splitting the work across two engines by difficulty), lets
/// any saved engine refine individual translations, and exports one translated package per source mod
/// with round-trip validation before reporting success (brief section 28).
/// </summary>
public sealed partial class EditorPageViewModel(
    IProjectService projectService,
    IProjectStringService projectStringService,
    ITranslationOrchestrator translationOrchestrator,
    ITokenProtector tokenProtector,
    IMultiEngineTranslator multiEngineTranslator,
    ITranslationProviderFactory providerFactory,
    IProviderProfileService profileService,
    IGlossaryService glossaryService,
    IPackageService packageService,
    IStringTableService stringTableService,
    IPathPickerService pathPicker,
    ExportManifestStore exportManifests,
    ITranslationMemory translationMemory) : ObservableObject, IDisposable
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

    /// <summary>Free-text style/tone instructions for this project's translations (brief section 22).</summary>
    [ObservableProperty]
    public partial string CustomInstructions { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsTranslating { get; set; }

    [ObservableProperty]
    public partial int ProgressCurrent { get; set; }

    [ObservableProperty]
    public partial int ProgressTotal { get; set; }

    /// <summary>Whether "Traduzir pendentes" may answer from translation memory instead of calling an engine.</summary>
    /// <remarks>
    /// On by default — reuse is what makes re-translating an updated mod cheap. It is exposed here
    /// because it is also the reason a bad run appears to be permanent: the memory is global and outlives
    /// the project, so deleting the project, re-importing the mod and translating again gives back the
    /// same translations without the engine being called once. Anyone comparing two models needs to be
    /// able to say "ignore what you already know and answer again".
    /// </remarks>
    [ObservableProperty]
    public partial bool ReuseTranslationMemory { get; set; } = true;

    /// <summary>
    /// Which saved profile the per-row refine button uses. Defaults to whatever Settings assigned to the
    /// refinement role, but stays overridable here so the user can try a different engine on one line
    /// without leaving the editor.
    /// </summary>
    [ObservableProperty]
    public partial string SelectedRefineProfileName { get; set; } = string.Empty;

    /// <summary>One-line summary of which engine does what, shown above the list so the setup is never a mystery.</summary>
    [ObservableProperty]
    public partial string PipelineSummary { get; set; } = string.Empty;

    private const string AllFilter = "Todas";

    /// <summary>
    /// How many strings to translate (and persist) before checkpointing. Chosen to line up with
    /// <see cref="BatchBuilder"/>'s default item cap, so one chunk is typically one round trip to the
    /// engine - large enough to not add meaningful overhead, small enough that a mod with thousands of
    /// strings shows real, frequent progress instead of one silent multi-minute wait, and that
    /// cancelling or a crash mid-run only loses the current chunk, not the whole job.
    /// </summary>
    private const int TranslationChunkSize = 100;

    /// <summary>
    /// The UI thread's queue, captured when the page loads.
    /// </summary>
    /// <remarks>
    /// <see cref="IMultiEngineTranslator"/> runs its lanes with <c>ConfigureAwait(false)</c>, so the
    /// per-chunk callback arrives on a thread pool thread. Touching an observable property or an
    /// <c>ObservableCollection</c> that XAML is bound to from there throws a bare
    /// <see cref="System.Runtime.InteropServices.COMException"/> with no message - which is exactly how
    /// the first real translation run failed. Everything that the UI observes has to come back through
    /// here; only the database write stays on the background thread, where it belongs.
    /// </remarks>
    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;

    private TranslationProject? _project;
    private readonly List<StringRowViewModel> _allRows = [];

    /// <summary>Every row matching the current filter and search — which is usually more than is shown.</summary>
    private List<StringRowViewModel> _filteredRows = [];

    private CancellationTokenSource? _translateCts;
    private TranslationPipelineSettings _pipeline = new();
    private IReadOnlyList<ProviderProfile> _profiles = [];

    /// <summary>Named here rather than inline so the filter and the export summary cannot drift apart.</summary>
    private const string IdentifiersFilter = "Identificadores internos";

    /// <summary>
    /// Traduções que passaram por boas mas parecem ter perdido pedaço do original — ver
    /// <see cref="TranslationSuspicion"/>.
    /// </summary>
    private const string SuspectFilter = "Suspeitas (motor pode ter simplificado)";

    public IReadOnlyList<string> FilterOptions { get; } =
        [AllFilter, "Pendentes", "Traduzidas", "Revisar", SuspectFilter, "Refinadas", "Editadas manualmente", IdentifiersFilter];

    public ObservableCollection<string> RefineProfileOptions { get; } = [];

    public ObservableCollection<StringRowViewModel> Strings { get; } = [];

    /// <summary>
    /// How many matching rows are actually put into <see cref="Strings"/>. A real project can hold well
    /// over a hundred thousand rows, and clearing and refilling an ObservableCollection that size on
    /// every keystroke froze the window for seconds - the ListView virtualizes rendering, but the
    /// collection change notifications themselves are the cost. The user is looking for a specific line,
    /// not scrolling past 149 thousand, so the list is capped and the true match count is reported.
    /// </summary>
    private const int MaximumVisibleRows = 500;

    /// <summary>Typing "shower" is six keystrokes; without this it was six full refilters of the whole project.</summary>
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

    private CancellationTokenSource? _searchDebounceCts;

    /// <summary>How many rows match the current filter, which may be more than are displayed.</summary>
    [ObservableProperty]
    public partial string FilterSummary { get; set; } = string.Empty;

    partial void OnSearchTextChanged(string value) => ScheduleFilter();

    partial void OnSelectedFilterChanged(string value) => ApplyFilter();

    /// <summary>Debounces the search box so a filter pass runs once the user pauses, not once per key.</summary>
    private void ScheduleFilter()
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        _searchDebounceCts = new CancellationTokenSource();
        var token = _searchDebounceCts.Token;

        _ = Task.Delay(SearchDebounce, token).ContinueWith(
            _ => ApplyFilter(),
            token,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void ApplyFilter()
    {
        IEnumerable<StringRowViewModel> filtered = _allRows;

        filtered = SelectedFilter switch
        {
            "Pendentes" => filtered.Where(r => r.Status == ProjectStringStatus.Pending),
            "Traduzidas" => filtered.Where(r => r.Status == ProjectStringStatus.Translated),
            "Revisar" => filtered.Where(r => r.Status == ProjectStringStatus.NeedsReview),
            "Refinadas" => filtered.Where(r => r.Status == ProjectStringStatus.Refined),
            "Editadas manualmente" => filtered.Where(r => r.Status == ProjectStringStatus.ManuallyEdited),

            // The queue this whole feature exists for. Not a status: these rows are stored as finished
            // work and most of them passed validation - the suspicion is computed from the text, so the
            // list stays correct the moment a row is repaired, with nothing to re-scan or migrate.
            SuspectFilter => filtered.Where(r => r.Suspicion.IsSuspect),

            // Visible on purpose: a mod carries hundreds of tuning identifiers that will sit at
            // "Pendente" forever, and a pending count that can never reach zero is its own small lie.
            IdentifiersFilter => filtered.Where(r => !StringTranslatability.IsWorthTranslating(r.Original)),
            _ => filtered,
        };

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(r =>
                r.Original.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                (r.Translation?.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ?? false));
        }

        // Kept whole, not just the visible slice: "marcar tudo que o filtro achou" and the export scope
        // both have to mean every match, or the user is silently back to ticking rows 500 at a time.
        _filteredRows = filtered.ToList();

        Strings.Clear();
        foreach (var row in _filteredRows.Take(MaximumVisibleRows))
        {
            Strings.Add(row);
        }

        FilterSummary = _filteredRows.Count > MaximumVisibleRows
            ? $"Mostrando {MaximumVisibleRows} de {_filteredRows.Count:N0} resultados - a exportação e o \"marcar tudo\" usam os {_filteredRows.Count:N0}."
            : $"{_filteredRows.Count:N0} resultado(s).";

        RefreshExportScopeSummary();
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread and completes when it has run.</summary>
    private Task RunOnUiAsync(Action action)
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource();
        if (!dispatcher.TryEnqueue(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }))
        {
            // The window is going away; failing loudly here beats a translation that silently stops
            // reporting progress while still burning provider quota.
            completion.SetException(new InvalidOperationException("A janela não aceitou mais atualizações de interface."));
        }

        return completion.Task;
    }

    [RelayCommand]
    private async Task LoadAsync(Guid projectId)
    {
        // Captured on the UI thread, which is where navigation invokes this command.
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        _project = await projectService.GetAsync(projectId);
        if (_project is null)
        {
            StatusText = "Projeto não encontrado.";
            return;
        }

        ProjectName = _project.Name;
        CustomInstructions = _project.CustomInstructions ?? string.Empty;

        await LoadPipelineAsync();

        var entries = await projectStringService.GetStringsAsync(projectId);
        _allRows.Clear();
        _allRows.AddRange(entries.Select(entry => new StringRowViewModel(entry) { SelectionChanged = RefreshExportScopeSummary }));
        ApplyFilter();

        var mods = _allRows.Select(r => r.SourceModName).Where(name => name is not null).Distinct().Count();
        StatusText = $"{_project.SourceLocale.Name} → {_project.TargetLocale.Name} · {_allRows.Count} string(s)"
            + (mods > 1 ? $" · {mods} mods" : string.Empty);
    }

    /// <summary>True when this project runs its own pipeline instead of following the global one.</summary>
    [ObservableProperty]
    public partial bool UsesProjectPipeline { get; set; }

    // Exposed as Visibility rather than composing BoolToVisibility(Not(...)) in XAML: nested x:Bind
    // function calls compile in Debug but blow up the Release XAML pass with an internal error.
    public Visibility ProjectPipelineButtonVisibility => UsesProjectPipeline ? Visibility.Visible : Visibility.Collapsed;

    public Visibility GlobalPipelineButtonVisibility => UsesProjectPipeline ? Visibility.Collapsed : Visibility.Visible;

    partial void OnUsesProjectPipelineChanged(bool value)
    {
        OnPropertyChanged(nameof(ProjectPipelineButtonVisibility));
        OnPropertyChanged(nameof(GlobalPipelineButtonVisibility));
    }

    /// <summary>Reads the saved profiles and role assignments so the editor can name the engines it will actually use.</summary>
    private async Task LoadPipelineAsync()
    {
        _profiles = await profileService.ListAsync();

        // Resolved for this project specifically: a project with its own override gets it, everyone
        // else follows the global setup.
        _pipeline = await profileService.GetPipelineSettingsAsync(_project?.Id);
        UsesProjectPipeline = _project is not null && await profileService.HasPipelineOverrideAsync(_project.Id);

        RefineProfileOptions.Clear();
        foreach (var profile in _profiles)
        {
            RefineProfileOptions.Add(profile.Name);
        }

        var refinementDefault = NameOf(_pipeline.ResolveProfileId(TranslationRole.Refinement));
        SelectedRefineProfileName = refinementDefault ?? (RefineProfileOptions.Count > 0 ? RefineProfileOptions[0] : string.Empty);

        PipelineSummary = BuildPipelineSummary();
    }

    private string BuildPipelineSummary()
    {
        if (_profiles.Count == 0)
        {
            return "Nenhum motor configurado ainda - vá em Configurações e adicione um perfil.";
        }

        var scope = UsesProjectPipeline ? "Config. deste projeto" : "Config. global";
        var primary = NameOf(_pipeline.PrimaryProfileId) ?? "(nenhum)";
        var review = NameOf(_pipeline.ResolveProfileId(TranslationRole.Review)) ?? "(nenhum)";

        var summary = $"{scope} · Principal: {primary} · Revisão: {review}";

        if (_pipeline.IsTieredRoutingUsable)
        {
            // A band pointing at the primary engine is not a separate lane - BuildRoutingPlanAsync skips
            // it - so listing it here would promise a split that will not happen.
            if (_pipeline.MediumProfileId != _pipeline.PrimaryProfileId && NameOf(_pipeline.MediumProfileId) is { } medium)
            {
                summary += $" · Médias (≥{_pipeline.MediumThreshold}): {medium}";
            }

            if (_pipeline.DifficultProfileId != _pipeline.PrimaryProfileId && NameOf(_pipeline.DifficultProfileId) is { } difficult)
            {
                summary += $" · Difíceis (≥{_pipeline.DifficultThreshold}): {difficult}";
            }
        }

        return summary;
    }

    /// <summary>Copies the currently effective pipeline into a project-specific override the user can then tune in Settings.</summary>
    [RelayCommand]
    private async Task UseProjectPipelineAsync()
    {
        if (_project is null)
        {
            return;
        }

        try
        {
            await profileService.SavePipelineSettingsAsync(_pipeline, _project.Id);
            await LoadPipelineAsync();
            StatusText = "Este projeto agora tem a própria configuração de motores. Ajuste-a em Configurações.";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao criar configuração do projeto ({exception.GetType().Name}): {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task UseGlobalPipelineAsync()
    {
        if (_project is null)
        {
            return;
        }

        try
        {
            await profileService.RemovePipelineOverrideAsync(_project.Id);
            await LoadPipelineAsync();
            StatusText = "Este projeto voltou a seguir a configuração global.";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao remover configuração do projeto ({exception.GetType().Name}): {exception.Message}";
        }
    }

    private string? NameOf(int? profileId) =>
        profileId is int id ? _profiles.FirstOrDefault(profile => profile.Id == id)?.Name : null;

    private int? IdOf(string profileName) =>
        _profiles.FirstOrDefault(profile => string.Equals(profile.Name, profileName, StringComparison.Ordinal))?.Id;

    /// <summary>
    /// Creates a glossary term from a line the user is looking at.
    /// </summary>
    /// <remarks>
    /// The glossary reaches every engine, including the chat bundle, and checks every reply — but it had
    /// no entry point except a separate page and a blank form, and stayed empty. Here the user is already
    /// looking at the word that keeps coming back wrong.
    ///
    /// <paramref name="original"/> and <paramref name="translation"/> are what the dialog collected, not
    /// the row verbatim: a glossary entry has to be a term, and a whole sentence pasted in as one would
    /// match nothing and quietly do nothing.
    /// </remarks>
    public async Task<bool> AddGlossaryTermAsync(string original, string translation, bool neverTranslate, bool globalScope)
    {
        if (_project is null || string.IsNullOrWhiteSpace(original))
        {
            return false;
        }

        try
        {
            var term = await glossaryService.AddTermAsync(
                original.Trim(),
                neverTranslate ? original.Trim() : translation.Trim(),
                neverTranslate,
                caseSensitive: false,
                notes: null,
                projectId: globalScope ? null : _project.Id);

            StatusText = neverTranslate
                ? $"\"{term.Original}\" nunca mais será traduzido{(globalScope ? " em nenhum projeto" : " neste projeto")}."
                : $"\"{term.Original}\" → \"{term.Translation}\" adicionado ao glossário{(globalScope ? " global" : " deste projeto")}. Vale para todos os motores e para o arquivo do chat.";
            return true;
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao adicionar ao glossário ({exception.GetType().Name}): {exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// Writes trustworthy pairs into the shared translation memory, filtered by
    /// <see cref="TranslationMemoryPolicy"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately never throws out of here. Memory is an optimisation for future mods; a failure to
    /// record one must not surface as "falha ao salvar edição" over work that was in fact saved.
    /// </remarks>
    private async Task<int> RememberAsync(IReadOnlyList<(string Original, string? Translation)> pairs, string provider)
    {
        if (_project is null)
        {
            return 0;
        }

        var entries = pairs
            .Where(pair => TranslationMemoryPolicy.ShouldRemember(pair.Original, pair.Translation))
            .Select(pair => new TranslationMemoryEntry(
                pair.Original,
                _project.SourceLocale,
                _project.TargetLocale,
                pair.Translation!,
                provider,
                ProfileName: null,
                DateTimeOffset.UtcNow))
            .ToList();

        if (entries.Count == 0)
        {
            return 0;
        }

        try
        {
            await translationMemory.SaveManyAsync(entries);
            return entries.Count;
        }
        catch (Exception exception)
        {
            StatusText = $"Tradução salva, mas não entrou na memória ({exception.GetType().Name}): {exception.Message}";
            return 0;
        }
    }

    /// <summary>Persists a manual edit made directly in the editor (brief sections 30/31). The previous translation, if any, is kept as a recoverable revision.</summary>
    public async Task SaveManualEditAsync(StringRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        // The box saves on LostFocus, which fires just from clicking into it and back out. Without
        // this guard that alone rewrote the row as "Editado manualmente", destroying its real status
        // and recording an empty revision.
        if (!row.HasUnsavedEdit)
        {
            return;
        }

        try
        {
            var newText = row.Translation ?? string.Empty;
            await projectStringService.UpdateTranslationAsync(row.Id, newText, ProjectStringStatus.ManuallyEdited, issueSummary: null);
            row.ApplyResult(newText, ProjectStringStatus.ManuallyEdited, issueSummary: null);

            // A line the user corrected by hand is the best translation the app will ever hold for that
            // text, and until now it was the only kind that was never remembered.
            await RememberAsync([(row.Original, newText)], TranslationMemoryPolicy.ManualProvider);
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao salvar edição ({exception.GetType().Name}): {exception.Message}";
        }
    }

    /// <summary>
    /// Sweeps every string flagged for review through the engine assigned to the review role — a
    /// deliberately different setting from the per-row refiner, so a slow expensive model can be used
    /// for a handful of clicks without also being turned loose on hundreds of strings.
    /// </summary>
    [RelayCommand]
    private async Task RefineNeedsReviewAsync()
    {
        if (_project is null)
        {
            return;
        }

        var profileId = _pipeline.ResolveProfileId(TranslationRole.Review);
        if (profileId is null)
        {
            StatusText = "Nenhum motor de revisão definido. Vá em Configurações e escolha um em \"Quem faz o quê\".";
            return;
        }

        var profileName = NameOf(profileId) ?? "motor de revisão";

        var targets = _allRows
            .Where(row => row.Status == ProjectStringStatus.NeedsReview && row.CanRefine)
            .ToList();

        if (targets.Count == 0)
        {
            StatusText = "Nenhuma string marcada para revisão com tradução para refinar.";
            return;
        }

        _translateCts = new CancellationTokenSource();
        var cancellationToken = _translateCts.Token;
        var refined = 0;

        try
        {
            IsBusy = true;
            IsTranslating = true;
            ProgressTotal = targets.Count;
            ProgressCurrent = 0;

            // Same checkpointing rationale as TranslateAsync: persist as we go so cancelling or a
            // crash never throws away refinements that already succeeded.
            foreach (var chunk in targets.Chunk(TranslationChunkSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                StatusText = $"Revisando com {profileName}... {ProgressCurrent}/{ProgressTotal}";

                refined += await RefineRowsAsync(chunk, profileId.Value, profileName, cancellationToken);
                ProgressCurrent += chunk.Length;
                ApplyFilter();
            }

            StatusText = $"Revisão concluída: {refined} de {targets.Count} string(s) melhoradas com {profileName}.";
        }
        catch (OperationCanceledException)
        {
            StatusText = $"Revisão cancelada em {ProgressCurrent}/{ProgressTotal} - o que já foi refinado está salvo ({refined} string(s)).";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao revisar ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            IsTranslating = false;
            _translateCts?.Dispose();
            _translateCts = null;
        }
    }

    /// <summary>How many rows the suspicion queue currently holds, for the button that empties it.</summary>
    public int SuspectCount => _allRows.Count(row => row.Suspicion.IsSuspect);

    /// <summary>
    /// Translates one suspect row again from the source alone, discarding the damaged draft.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately not a refinement, and this was measured rather than reasoned about.</b> The first
    /// version of this button did call <see cref="ITranslationOrchestrator.RefineAsync"/> — it is the
    /// obvious choice, the machinery already existed, and it does not work. Run against sixteen real rows
    /// from this project with Qwen3-8B, refinement fixed <b>zero</b> of the eight damaged ones: shown a
    /// draft, the model reproduced it. <c>---------- Settings ----------</c> came back as
    /// <c>- Não, não, não. Configurações...</c>, character for character, and one translation that was
    /// already fine was made worse. Translating the same rows from scratch fixed all eight.
    ///
    /// The reason is the premise. Refinement exists for a draft that is mostly right and needs polish,
    /// and it puts that draft in front of the model on purpose. The suspicion queue is the exact opposite
    /// population: these drafts are wrong by omission, so showing one is teaching the model the wrong
    /// answer and asking it not to stray far from it. "Refinar" stays as it is, for the case it was built
    /// for. Repensar throws the draft away — the old text is kept as a revision, as always.
    /// </remarks>
    public Task RethinkRowAsync(StringRowViewModel row) => RetranslateOneRowAsync(row, ReasoningMode.On);

    /// <summary>Sends one string back through the chosen engine as it is configured.</summary>
    public Task RefineRowAsync(StringRowViewModel row) => RefineOneRowAsync(row, reasoningOverride: null, "Refinado");

    /// <summary>
    /// Runs every suspect row through the refine engine with reasoning on.
    /// </summary>
    /// <remarks>
    /// Deliberately the refine profile rather than the review one: the review sweep is for what the
    /// validator flagged, and this queue is the opposite case - rows that validated cleanly and are
    /// wrong anyway. The user picks the engine in the same box either way, so a paid API can take this
    /// pass while the local model keeps the bulk work, which is the cheap version of "spend the good
    /// model only where it matters".
    /// </remarks>
    /// <param name="useReasoning">
    /// Forces the model to think for every row. Off by default, and that default is measured rather than
    /// assumed: on this queue's actual population - content the engine dropped - Qwen3-8B repaired every
    /// case with reasoning off, and reasoning bought nothing for twenty times the clock (docs/LOCAL_MODELS.md,
    /// "Qwen3-8B"). Reasoning earns its cost on the other failure, a short label whose sense is ambiguous,
    /// which is what the per-row button is for.
    /// </param>
    public async Task RethinkSuspectsAsync(bool useReasoning)
    {
        if (_project is null)
        {
            return;
        }

        var profileId = IdOf(SelectedRefineProfileName);
        if (profileId is null)
        {
            StatusText = "Escolha um motor no seletor \"Refinar linha a linha com\" — é ele que vai repensar as suspeitas.";
            return;
        }

        var targets = _allRows.Where(row => row.Suspicion.IsSuspect && row.CanRefine).ToList();
        if (targets.Count == 0)
        {
            StatusText = "Nenhuma tradução suspeita neste projeto. Nada a repensar.";
            return;
        }

        _translateCts = new CancellationTokenSource();
        var cancellationToken = _translateCts.Token;
        var improved = 0;

        try
        {
            IsBusy = true;
            IsTranslating = true;
            ProgressTotal = targets.Count;
            ProgressCurrent = 0;

            // Same checkpointing as every other long run here: a cancelled pass keeps what it already
            // fixed. It matters more than usual with reasoning on, because each string costs real time.
            // Null, not ReasoningMode.Off: leaving the override unset lets a profile the user deliberately
            // set to "ligado" stay that way, instead of this pass quietly overruling it in the other
            // direction.
            var reasoningOverride = useReasoning ? ReasoningMode.On : (ReasoningMode?)null;
            var reasoningNote = useReasoning ? ", raciocínio ligado" : string.Empty;

            foreach (var chunk in targets.Chunk(TranslationChunkSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                StatusText = $"Repensando com {SelectedRefineProfileName}{reasoningNote}... {ProgressCurrent}/{ProgressTotal}";

                improved += await RetranslateRowsAsync(chunk, profileId.Value, SelectedRefineProfileName, cancellationToken, reasoningOverride);
                ProgressCurrent += chunk.Length;
                ApplyFilter();
            }

            var remaining = SuspectCount;
            StatusText = $"Repensadas: {improved} de {targets.Count} linha(s) mudaram com {SelectedRefineProfileName}. "
                + (remaining == 0
                    ? "A fila de suspeitas está vazia."
                    : $"Ainda restam {remaining:N0} na fila - as que o motor devolveu igual continuam marcadas.");
        }
        catch (OperationCanceledException)
        {
            StatusText = $"Repensar cancelado em {ProgressCurrent}/{ProgressTotal} - o que já melhorou está salvo ({improved} linha(s)).";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao repensar ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            IsTranslating = false;
            _translateCts?.Dispose();
            _translateCts = null;
        }
    }

    private Task RefineOneRowAsync(StringRowViewModel row, ReasoningMode? reasoningOverride, string doneVerb) =>
        RunOnOneRowAsync(row, doneVerb, (rows, profileId, name, token) =>
            RefineRowsAsync(rows, profileId, name, token, reasoningOverride));

    private Task RetranslateOneRowAsync(StringRowViewModel row, ReasoningMode? reasoningOverride) =>
        RunOnOneRowAsync(row, "Repensado", (rows, profileId, name, token) =>
            RetranslateRowsAsync(rows, profileId, name, token, reasoningOverride));

    private async Task RunOnOneRowAsync(
        StringRowViewModel row,
        string doneVerb,
        Func<IReadOnlyList<StringRowViewModel>, int, string, CancellationToken, Task<int>> run)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (_project is null || row.IsRefining)
        {
            return;
        }

        var profileId = IdOf(SelectedRefineProfileName);
        if (profileId is null)
        {
            StatusText = "Escolha um motor de refino no seletor acima (ou crie um perfil em Configurações).";
            return;
        }

        try
        {
            row.IsRefining = true;
            var before = row.Translation;
            var refined = await run([row], profileId.Value, SelectedRefineProfileName, CancellationToken.None);

            // "Came back identical" and "came back with nothing" are different outcomes and the user
            // has to be able to tell them apart: the first means the engine simply agreed with the
            // existing translation (very common when refining Argos with Argos), the second means it
            // failed. Reporting both as "não retornou nada" reads as a bug when it is not one.
            StatusText = refined > 0
                ? $"{doneVerb} com {SelectedRefineProfileName}."
                : string.Equals(before, row.Translation, StringComparison.Ordinal)
                    ? $"{SelectedRefineProfileName} devolveu exatamente a mesma tradução - nada a melhorar aqui. Experimente outro motor no seletor acima."
                    : $"{SelectedRefineProfileName} não retornou uma versão utilizável para esta string.";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao refinar ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            row.IsRefining = false;
        }
    }

    /// <summary>
    /// Translates rows again from the source alone. The engine never sees what is stored.
    /// </summary>
    /// <remarks>
    /// The body of "Repensar". Translation memory is bypassed for the same reason the draft is withheld:
    /// the damaged text is in there too (the bulk run wrote it), so consulting memory would hand back the
    /// very translation being thrown away — the same trap as DEC-038, one layer down.
    /// </remarks>
    private async Task<int> RetranslateRowsAsync(
        IReadOnlyList<StringRowViewModel> rows,
        int profileId,
        string profileName,
        CancellationToken cancellationToken,
        ReasoningMode? reasoningOverride = null)
    {
        var provider = await providerFactory.CreateForProfileAsync(profileId, reasoningOverride, cancellationToken);
        if (provider is null)
        {
            StatusText = $"O perfil \"{profileName}\" não existe mais. Recarregue a página ou revise Configurações.";
            return 0;
        }

        var glossaryTerms = await glossaryService.GetTermsAsync(_project!.Id, cancellationToken);
        var items = rows
            .Select(row => new TranslationSourceItem(row.Id.ToString(CultureInfo.InvariantCulture), row.Original))
            .ToList();

        var outcomes = await translationOrchestrator.TranslateAsync(
            items, provider, _project.SourceLocale, _project.TargetLocale, glossaryTerms, _project.CustomInstructions,
            reuseTranslationMemory: false, cancellationToken);

        var updates = new List<ProjectStringTranslationUpdate>();
        var rowsById = rows.ToDictionary(row => row.Id);

        foreach (var outcome in outcomes)
        {
            var stringId = int.Parse(outcome.Id, CultureInfo.InvariantCulture);
            if (outcome.TranslatedText is null)
            {
                continue;
            }

            // Byte-identical means the engine independently arrived at what was already stored, which is
            // not a change and must not flip the row's status or write a pointless revision.
            if (rowsById.TryGetValue(stringId, out var row) && string.Equals(row.Translation, outcome.TranslatedText, StringComparison.Ordinal))
            {
                continue;
            }

            var status = outcome.Success ? ProjectStringStatus.Refined : ProjectStringStatus.NeedsReview;
            updates.Add(new ProjectStringTranslationUpdate(
                stringId, outcome.TranslatedText, status, ValidationIssueText.Summarize(outcome.Issues)));
        }

        await projectStringService.UpdateTranslationsAsync(updates, cancellationToken);

        foreach (var update in updates)
        {
            if (rowsById.TryGetValue(update.StringEntryId, out var row))
            {
                row.ApplyResult(update.TranslatedText, update.Status, update.IssueSummary);
            }
        }

        return updates.Count;
    }

    /// <summary>The shared body of every refinement path. Returns how many rows actually came back improved.</summary>
    private async Task<int> RefineRowsAsync(
        IReadOnlyList<StringRowViewModel> rows,
        int profileId,
        string profileName,
        CancellationToken cancellationToken,
        ReasoningMode? reasoningOverride = null)
    {
        var provider = await providerFactory.CreateForProfileAsync(profileId, reasoningOverride, cancellationToken);
        if (provider is null)
        {
            StatusText = $"O perfil \"{profileName}\" não existe mais. Recarregue a página ou revise Configurações.";
            return 0;
        }

        var glossaryTerms = await glossaryService.GetTermsAsync(_project!.Id, cancellationToken);

        var items = rows
            .Select(row => new RefinementSourceItem(row.Id.ToString(CultureInfo.InvariantCulture), row.Original, row.Translation))
            .ToList();

        var outcomes = await translationOrchestrator.RefineAsync(
            items, provider, _project.SourceLocale, _project.TargetLocale, glossaryTerms, _project.CustomInstructions, cancellationToken);

        var updates = new List<ProjectStringTranslationUpdate>();
        var rowsById = rows.ToDictionary(row => row.Id);

        foreach (var outcome in outcomes)
        {
            var stringId = int.Parse(outcome.Id, CultureInfo.InvariantCulture);
            if (outcome.TranslatedText is null)
            {
                continue;
            }

            // A refinement that comes back byte-identical is not a failure, but it is also not an
            // improvement - leaving the row's status alone keeps "still needs review" honest instead
            // of marking it done because a second engine agreed with the first.
            if (rowsById.TryGetValue(stringId, out var row) && string.Equals(row.Translation, outcome.TranslatedText, StringComparison.Ordinal))
            {
                continue;
            }

            var status = outcome.Success ? ProjectStringStatus.Refined : ProjectStringStatus.NeedsReview;
            var issueSummary = ValidationIssueText.Summarize(outcome.Issues);
            updates.Add(new ProjectStringTranslationUpdate(stringId, outcome.TranslatedText, status, issueSummary));
        }

        await projectStringService.UpdateTranslationsAsync(updates, cancellationToken);

        foreach (var update in updates)
        {
            if (rowsById.TryGetValue(update.StringEntryId, out var row))
            {
                row.ApplyResult(update.TranslatedText, update.Status, update.IssueSummary);
            }
        }

        return updates.Count;
    }

    // ---- Ida e volta por arquivo de texto para um chat de IA ----

    public const string ScopeUntranslated = "O que ainda falta traduzir";
    public const string ScopeEverything = "O mod inteiro";
    public const string ScopeFilter = "O que o filtro achou";
    public const string ScopeTicked = "Só as linhas marcadas";

    public IReadOnlyList<string> ExportScopeOptions { get; } = [ScopeUntranslated, ScopeEverything, ScopeFilter, ScopeTicked];

    /// <summary>
    /// What the next chat export covers. An explicit choice, because the old behaviour — the ticked rows,
    /// or else whatever the list happened to be showing — quietly capped an export at the 500 rows the
    /// list renders, and a mod with four thousand strings has no way to notice that from the outside.
    /// </summary>
    [ObservableProperty]
    public partial string SelectedExportScope { get; set; } = ScopeUntranslated;

    /// <summary>Says, before the user commits, exactly how many strings and how many files this will be.</summary>
    [ObservableProperty]
    public partial string ExportScopeSummary { get; set; } = string.Empty;

    /// <summary>
    /// How many strings go in one file. Chat models truncate long replies without saying so, and a
    /// truncated reply is the one failure that costs real work, so the export splits rather than hoping.
    /// Zero means one single file no matter the size — the user asked to be able to say that.
    /// </summary>
    [ObservableProperty]
    public partial int MaximumStringsPerFile { get; set; } = 1000;

    /// <summary>NumberBox speaks in doubles and hands back NaN when its box is emptied; the app counts whole strings.</summary>
    public double MaximumStringsPerFileValue
    {
        get => MaximumStringsPerFile;
        set => MaximumStringsPerFile = double.IsNaN(value) ? 0 : (int)Math.Clamp(value, 0, 100_000);
    }

    /// <summary>
    /// Whether tuning identifiers go to the chat model too. Off by default: they are not text, the model
    /// can only hand them back unchanged, and they were a fifth of some exports. On is here because
    /// "exporta tudo" has to be able to actually mean everything.
    /// </summary>
    [ObservableProperty]
    public partial bool IncludeInternalIdentifiers { get; set; }

    partial void OnSelectedExportScopeChanged(string value) => RefreshExportScopeSummary();

    partial void OnMaximumStringsPerFileChanged(int value)
    {
        OnPropertyChanged(nameof(MaximumStringsPerFileValue));
        RefreshExportScopeSummary();
    }

    partial void OnIncludeInternalIdentifiersChanged(bool value) => RefreshExportScopeSummary();

    /// <summary>The rows the chosen scope covers, before anything is filtered out of them.</summary>
    private List<StringRowViewModel> ResolveScopeRows() => SelectedExportScope switch
    {
        ScopeTicked => [.. _allRows.Where(row => row.IsSelected)],
        ScopeFilter => [.. _filteredRows],
        ScopeEverything => [.. _allRows],
        _ => [.. _allRows.Where(row => row.Status is ProjectStringStatus.Pending or ProjectStringStatus.NeedsReview)],
    };

    /// <summary>
    /// Splits a scope into what will be sent and what will not, so both the preview and the export itself
    /// answer the same question the same way.
    /// </summary>
    private (List<StringRowViewModel> Sending, int SkippedIdentifiers) PlanExport()
    {
        var rows = ResolveScopeRows();

        if (IncludeInternalIdentifiers)
        {
            return (rows, 0);
        }

        var sending = rows.Where(row => StringTranslatability.IsWorthTranslating(row.Original)).ToList();
        return (sending, rows.Count - sending.Count);
    }

    /// <summary>Set while ticking thousands of rows at once, so the summary is recomputed once instead of once per row.</summary>
    private bool _suspendScopeSummary;

    private void RefreshExportScopeSummary()
    {
        if (_suspendScopeSummary)
        {
            return;
        }

        // Both summaries answer "what am I about to act on", change on the same events, and are
        // suspended by the same flag during a bulk tick, so they are refreshed together.
        RefreshSelectionSummary();

        var (sending, skipped) = PlanExport();

        if (sending.Count == 0)
        {
            ExportScopeSummary = skipped > 0
                ? $"Nada a exportar - as {skipped} linha(s) desse recorte são identificadores internos."
                : "Nada a exportar nesse recorte.";
            return;
        }

        var parts = MaximumStringsPerFile > 0
            ? (sending.Count + MaximumStringsPerFile - 1) / MaximumStringsPerFile
            : 1;

        var summary = parts > 1
            ? $"{sending.Count:N0} texto(s) → {parts} arquivo(s) de até {MaximumStringsPerFile:N0}"
            : $"{sending.Count:N0} texto(s) → 1 arquivo";

        if (skipped > 0)
        {
            summary += $" · {skipped:N0} identificador(es) interno(s) fora";
        }

        ExportScopeSummary = summary;
    }

    // ---- Acoes em lote sobre as linhas marcadas ----

    /// <summary>How many rows are ticked, so a bulk action says what it is about to touch.</summary>
    [ObservableProperty]
    public partial string SelectionSummary { get; set; } = "Nenhuma linha marcada.";

    /// <summary>True while at least one row is ticked, gating the bulk actions.</summary>
    [ObservableProperty]
    public partial bool HasSelection { get; set; }

    private List<StringRowViewModel> SelectedRows() => [.. _allRows.Where(row => row.IsSelected)];

    private void RefreshSelectionSummary()
    {
        var count = _allRows.Count(row => row.IsSelected);
        HasSelection = count > 0;
        SelectionSummary = count == 0
            ? "Nenhuma linha marcada."
            : $"{count:N0} linha(s) marcada(s).";
    }

    /// <summary>
    /// How many of the ticked rows a revert would actually change, for the confirmation text.
    /// </summary>
    public int SelectedWithTranslationCount => _allRows.Count(row => row.IsSelected && !string.IsNullOrEmpty(row.Translation));

    /// <summary>
    /// Throws away the translations of every ticked row and puts them back to pending.
    /// </summary>
    /// <remarks>
    /// The fix for a run that failed the same way across many strings — a model that invented a marker in
    /// twenty-nine lines, say. Correcting those by hand is not a review, it is data entry, and exporting
    /// them as they stand ships a broken string table. Reverting is safe in a way that keeping them is
    /// not: the export writes the original text where there is no translation, and the discarded text is
    /// kept as a revision.
    /// </remarks>
    public async Task<int> RevertSelectedToOriginalAsync()
    {
        var rows = SelectedRows();
        if (rows.Count == 0)
        {
            return 0;
        }

        try
        {
            IsBusy = true;

            var cleared = await projectStringService.ClearTranslationsAsync([.. rows.Select(row => row.Id)]);

            foreach (var row in rows)
            {
                row.ApplyResult(null, ProjectStringStatus.Pending, issueSummary: null);
            }

            ApplyFilter();
            StatusText = cleared == 0
                ? "Nenhuma das linhas marcadas tinha tradução para descartar."
                : $"{cleared:N0} linha(s) voltaram ao texto original e estão pendentes de novo. A tradução descartada ficou no histórico de cada linha.";
            return cleared;
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao reverter ({exception.GetType().Name}): {exception.Message}";
            return 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Accepts the ticked rows as they are: the translation stays, the review flag goes.
    /// </summary>
    /// <remarks>
    /// The other half of triaging a review queue. Some flags are the validator being right and some are
    /// it being cautious - "Strip Poker!" coming back as "Strip Poker!" is correct, not a failure - and
    /// without this the only way to clear one was to retype the translation over itself.
    /// </remarks>
    public async Task<int> AcceptSelectedAsync()
    {
        var rows = SelectedRows().Where(row => !string.IsNullOrWhiteSpace(row.Translation)).ToList();
        if (rows.Count == 0)
        {
            StatusText = "Nenhuma das linhas marcadas tem tradução para aceitar.";
            return 0;
        }

        try
        {
            IsBusy = true;

            var updates = rows
                .Select(row => new ProjectStringTranslationUpdate(row.Id, row.Translation!, ProjectStringStatus.ManuallyEdited, null))
                .ToList();

            await projectStringService.UpdateTranslationsAsync(updates);

            foreach (var row in rows)
            {
                row.ApplyResult(row.Translation, ProjectStringStatus.ManuallyEdited, issueSummary: null);
            }

            // Accepting is a human vouching for the text, so it earns a place in the memory on the same
            // footing as an edit typed by hand.
            var remembered = await RememberAsync(
                [.. rows.Select(row => (row.Original, row.Translation))],
                TranslationMemoryPolicy.ManualProvider);

            ApplyFilter();
            StatusText = $"{rows.Count:N0} linha(s) aceitas como estão"
                + (remembered > 0 ? $" · {remembered:N0} guardada(s) na memória de tradução." : ".");
            return rows.Count;
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao aceitar ({exception.GetType().Name}): {exception.Message}";
            return 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Re-checks every stored translation for marker damage and flags what is broken.
    /// </summary>
    /// <remarks>
    /// A translation is validated once, when it arrives, and never looked at again — so a defect the
    /// validator could not see at the time stays invisible forever, stored as finished work. That is
    /// exactly what happened when a model answered token-free strings with the bare marker stem
    /// <c>__TS4_TOKEN_</c>: nine strings in one mod were saved as "Traduzido" with the project reporting
    /// nothing to review, and the only way to find them was reading the whole mod.
    ///
    /// Deliberately narrow: only marker integrity, never the softer checks. Re-running the full
    /// validator over a finished project would bury the corrupted lines under thousands of "voltou igual
    /// ao original" notes on labels that are fine.
    /// </remarks>
    [RelayCommand]
    private async Task CheckMarkersAsync()
    {
        if (_project is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = "Conferindo os marcadores de todas as linhas traduzidas...";

            var damaged = _allRows
                .Where(row => !string.IsNullOrEmpty(row.Translation))
                .Select(row => (Row: row, Restore: tokenProtector.Restore(row.Translation!, tokenProtector.Protect(row.Original))))
                .Where(pair => pair.Restore.Issues.Any(issue => issue.IsCritical))
                .ToList();

            if (damaged.Count == 0)
            {
                StatusText = $"Nenhum marcador quebrado em {_allRows.Count(row => !string.IsNullOrEmpty(row.Translation)):N0} linha(s) traduzida(s).";
                return;
            }

            await projectStringService.UpdateTranslationsAsync(
                [.. damaged.Select(pair => new ProjectStringTranslationUpdate(
                    pair.Row.Id,
                    pair.Row.Translation!,
                    ProjectStringStatus.NeedsReview,
                    ValidationIssueText.Describe(ValidationIssueCode.ModifiedPlaceholder)))]);

            foreach (var (row, _) in damaged)
            {
                row.ApplyResult(row.Translation, ProjectStringStatus.NeedsReview,
                    ValidationIssueText.Describe(ValidationIssueCode.ModifiedPlaceholder));
            }

            SelectedFilter = "Revisar";
            ApplyFilter();
            StatusText = $"{damaged.Count:N0} linha(s) com marcador quebrado foram marcadas para revisão. "
                + "Marque tudo do filtro e use \"Reverter para o original\" - o texto quebrado fica no histórico.";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao conferir marcadores ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Ticks every row the current filter matched, not just the ones the list is rendering.</summary>
    [RelayCommand]
    private void SelectAllMatching()
    {
        _suspendScopeSummary = true;
        foreach (var row in _filteredRows)
        {
            row.IsSelected = true;
        }

        _suspendScopeSummary = false;
        RefreshExportScopeSummary();
        StatusText = $"{_filteredRows.Count:N0} linha(s) do filtro atual marcada(s).";
    }

    [RelayCommand]
    private void ClearSelection()
    {
        _suspendScopeSummary = true;
        foreach (var row in _allRows)
        {
            row.IsSelected = false;
        }

        _suspendScopeSummary = false;
        RefreshExportScopeSummary();
        StatusText = "Seleção limpa.";
    }

    /// <summary>
    /// Writes the chosen scope as one or more text files the user pastes into ChatGPT, Claude, Gemini or
    /// Grok, and records what went into each one so the import can check nothing came back missing.
    /// </summary>
    [RelayCommand]
    private async Task ExportBundleAsync()
    {
        if (_project is null)
        {
            return;
        }

        try
        {
            IsBusy = true;

            var (sending, skipped) = PlanExport();
            if (sending.Count == 0)
            {
                StatusText = skipped > 0
                    ? $"Nada foi exportado: as {skipped} linha(s) desse recorte são identificadores internos do mod. Marque \"incluir identificadores\" se quiser mandá-los mesmo assim."
                    : "Nada a exportar nesse recorte - troque o alcance ou o filtro.";
                return;
            }

            var items = sending.Select(row => new BundleItem(row.Id, row.Original, row.Translation)).ToList();

            // The chat model is a translation engine like any other, and it was the only one the app
            // never told about the glossary or the project's own instructions.
            var glossary = await glossaryService.GetTermsAsync(_project.Id);
            var parts = BundleSplitter.Split(
                _project.Name, _project.SourceLocale, _project.TargetLocale, items, MaximumStringsPerFile,
                glossary, _project.CustomInstructions);

            var suggested = $"{_project.Name}_{sending.Count}-textos.txt";
            var chosen = await pathPicker.PickSaveTextFileAsync(SanitizeFileName(suggested));
            if (chosen is null)
            {
                return;
            }

            var written = new List<string>();
            var manifestParts = new List<ExportManifestPart>();

            foreach (var part in parts)
            {
                var path = BundleSplitter.NameFor(chosen, part);

                // UTF-8 with a BOM: the user opens this in Notepad and pastes it into a browser, and
                // without the mark Notepad has historically guessed the encoding wrong and mangled the
                // accents.
                await File.WriteAllTextAsync(path, TranslationBundleWriter.Write(part), new System.Text.UTF8Encoding(true));

                written.Add(Path.GetFileName(path));
                manifestParts.Add(new ExportManifestPart(part.PartNumber, Path.GetFileName(path), [.. part.Items.Select(item => item.Id)]));
            }

            // The save picker creates the file the user named before handing back the path. On a split
            // export nothing is written to it, so it would sit next to the real parts as an empty file
            // with the most obvious name of the lot - the first one they would open.
            if (parts.Count > 1 && File.Exists(chosen) && new FileInfo(chosen).Length == 0)
            {
                File.Delete(chosen);
            }

            await exportManifests.SaveAsync(new ExportManifest(_project.Id, DateTimeOffset.Now, SelectedExportScope, manifestParts));

            _sessionExports.Clear();
            foreach (var item in items)
            {
                _sessionExports[item.Id] = item;
            }

            StatusText = BuildExportSummary(written, sending.Count, skipped, chosen);
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao exportar textos ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string BuildExportSummary(List<string> written, int stringCount, int skipped, string chosenPath)
    {
        var folder = Path.GetDirectoryName(chosenPath);

        var summary = written.Count == 1
            ? $"{stringCount:N0} texto(s) exportados para {written[0]}."
            : $"{stringCount:N0} texto(s) exportados em {written.Count} arquivos ({written[0]} … {written[^1]}) em {folder}. Envie um por vez ao chat.";

        if (skipped > 0)
        {
            summary += $" {skipped:N0} identificador(es) interno(s) do mod ficaram de fora porque não são texto traduzível.";
        }

        return summary + " Traga cada resposta de volta em \"Importar do chat\" - pode selecionar todos os arquivos de uma vez.";
    }

    /// <summary>
    /// Reads chat replies back in, one or several at a time, and reports in full what happened.
    /// </summary>
    /// <remarks>
    /// Matching is against the strings that were actually exported, recorded by
    /// <see cref="ExportManifestStore"/> — that is what lets the app say "23 de 1000 não voltaram" when a
    /// model truncates its reply. With no manifest it falls back to the whole project, which still matches
    /// every id correctly but cannot judge completeness, and says so.
    /// </remarks>
    [RelayCommand]
    private async Task ImportBundleAsync()
    {
        if (_project is null)
        {
            return;
        }

        try
        {
            IsBusy = true;

            var paths = await pathPicker.PickOpenTextFilesAsync();
            if (paths.Count == 0)
            {
                return;
            }

            var manifest = await exportManifests.LoadAsync(_project.Id);

            // Read fresh rather than reusing what the export sent: the user may well have fixed a term
            // in the glossary precisely because the last reply got it wrong.
            var glossary = await glossaryService.GetTermsAsync(_project.Id);
            var translations = new Dictionary<int, BundleTranslation>();
            var problems = new List<BundleProblem>();
            var unmatchedFiles = 0;

            foreach (var path in paths)
            {
                var reply = await File.ReadAllTextAsync(path);
                var name = Path.GetFileName(path);

                var bundle = BundleFor(reply, manifest, glossary, ref unmatchedFiles);
                var result = TranslationBundleReader.Read(reply, bundle);

                foreach (var translation in result.Translations)
                {
                    translations[translation.Id] = translation;
                }

                // Prefixed with the file when there is more than one, so a problem is traceable to the
                // reply that caused it instead of floating in a merged list.
                problems.AddRange(paths.Count == 1
                    ? result.Problems
                    : result.Problems.Select(problem => problem with { Message = $"[{name}] {problem.Message}" }));
            }

            if (unmatchedFiles > 0)
            {
                problems.Add(new BundleProblem(BundleProblemSeverity.Info, BundleProblemKind.MissingStrings,
                    $"{unmatchedFiles} arquivo(s) não correspondem a nenhuma exportação registrada deste projeto. " +
                    "As traduções foram aplicadas pelos ids, mas o app não tem como saber se a resposta veio completa."));
            }

            var merged = new BundleImportResult([.. translations.Values], problems);
            ShowImportReport(paths, merged);

            if (merged.Translations.Count == 0)
            {
                StatusText = "Nada foi importado. " + merged.Summary;
                return;
            }

            // Anything that came back with placeholders missing, or that was matched without an id, is
            // stored for review rather than as a finished translation - the user has to look at it.
            var updates = merged.Translations
                .Select(translation => new ProjectStringTranslationUpdate(
                    translation.Id,
                    translation.Translation,
                    translation.IsTrustworthy ? ProjectStringStatus.ManuallyEdited : ProjectStringStatus.NeedsReview,
                    DescribeImportIssue(translation)))
                .ToList();

            await projectStringService.UpdateTranslationsAsync(updates);

            var originalsById = _allRows.ToDictionary(row => row.Id, row => row.Original);

            foreach (var update in updates)
            {
                _allRows.FirstOrDefault(row => row.Id == update.StringEntryId)
                    ?.ApplyResult(update.TranslatedText, update.Status, update.IssueSummary);
            }

            // Only what came back trustworthy is worth teaching to every future mod - the same bar the
            // automatic pipeline applies when it refuses to memorise a result that raised an issue.
            var remembered = await RememberAsync(
                [.. merged.Translations
                    .Where(translation => translation.IsTrustworthy && originalsById.ContainsKey(translation.Id))
                    .Select(translation => (originalsById[translation.Id], (string?)translation.Translation))],
                TranslationMemoryPolicy.ChatProvider);

            ApplyFilter();

            var source = paths.Count == 1 ? Path.GetFileName(paths[0]) : $"{paths.Count} arquivos";
            StatusText = $"{source}: {merged.Summary}"
                + (remembered > 0 ? $" · {remembered:N0} guardada(s) na memória de tradução." : ".");
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao importar ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Why a chat import was parked for review, in the words the row will show.</summary>
    private static string? DescribeImportIssue(BundleTranslation translation) => translation switch
    {
        { IsTrustworthy: true } => null,
        { PlaceholdersMatch: false } => "Importado do chat com marcadores faltando ou alterados.",
        { GlossaryRespected: false } => "Importado do chat sem respeitar o glossário.",
        _ => "Importado do chat sem id - confira se caiu na string certa.",
    };

    /// <summary>
    /// The set of strings one reply file should be checked against: the exported part it belongs to when
    /// that is known, otherwise everything the project has.
    /// </summary>
    private TranslationBundle BundleFor(
        string reply,
        ExportManifest? manifest,
        IReadOnlyList<GlossaryTerm> glossary,
        ref int unmatchedFiles)
    {
        if (manifest is not null && MatchingPart(reply, manifest) is { } part)
        {
            var byId = _allRows.ToDictionary(row => row.Id);
            var items = part.StringIds
                .Where(byId.ContainsKey)
                .Select(id => _sessionExports.TryGetValue(id, out var exported)
                    ? exported
                    : new BundleItem(id, byId[id].Original, byId[id].Translation))
                .ToList();

            if (items.Count > 0)
            {
                return new TranslationBundle(
                    _project!.Name, _project.SourceLocale, _project.TargetLocale, items,
                    part.PartNumber, manifest.Parts.Count, glossary);
            }
        }

        unmatchedFiles++;
        return new TranslationBundle(
            _project!.Name,
            _project.SourceLocale,
            _project.TargetLocale,
            [.. _allRows.Select(row => new BundleItem(row.Id, row.Original, row.Translation))],
            Glossary: glossary);
    }

    /// <summary>
    /// Which exported part a reply came from, decided by how much of that part's ids the reply carries.
    /// A model that drops a third of the blocks still has to land on the right part, so this is a best
    /// overlap rather than an exact match — but a file sharing almost nothing with a part is not it.
    /// </summary>
    private static ExportManifestPart? MatchingPart(string reply, ExportManifest manifest)
    {
        var replyIds = new HashSet<int>();
        foreach (var line in reply.ReplaceLineEndings("\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#') && int.TryParse(trimmed[1..], out var id))
            {
                replyIds.Add(id);
            }
        }

        if (replyIds.Count == 0)
        {
            return null;
        }

        var best = manifest.Parts
            .Select(part => (Part: part, Overlap: part.StringIds.Count(replyIds.Contains)))
            .OrderByDescending(candidate => candidate.Overlap)
            .FirstOrDefault();

        return best.Overlap >= Math.Max(1, replyIds.Count / 2) ? best.Part : null;
    }

    // ---- Relatório de importação ----

    public ObservableCollection<ImportProblemGroupViewModel> ImportProblems { get; } = [];

    [ObservableProperty]
    public partial string ImportSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasImportReport { get; set; }

    /// <summary>Drives the report banner's colour: an import with a real problem must not look like a clean one.</summary>
    [ObservableProperty]
    public partial InfoBarSeverity ImportSeverity { get; set; } = InfoBarSeverity.Success;

    /// <summary>The full text of the last import, held so it can be written out on demand.</summary>
    private string _lastImportReport = string.Empty;

    private void ShowImportReport(IReadOnlyList<string> paths, BundleImportResult result)
    {
        ImportProblems.Clear();
        foreach (var group in BundleImportReport.Group(result.Problems))
        {
            ImportProblems.Add(new ImportProblemGroupViewModel(group));
        }

        ImportSummary = result.Summary;
        ImportSeverity = result.ErrorCount > 0
            ? InfoBarSeverity.Error
            : result.WarningCount > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success;

        _lastImportReport = BundleImportReport.Write([.. paths.Select(Path.GetFileName)!], result);
        HasImportReport = true;
    }

    /// <summary>Writes the complete list to a file — the part a status line can never hold.</summary>
    [RelayCommand]
    private async Task SaveImportReportAsync()
    {
        if (_lastImportReport.Length == 0)
        {
            return;
        }

        try
        {
            var suggested = SanitizeFileName($"{ProjectName}_relatorio-importacao.txt");
            var path = await pathPicker.PickSaveTextFileAsync(suggested);
            if (path is null)
            {
                return;
            }

            await File.WriteAllTextAsync(path, _lastImportReport, new System.Text.UTF8Encoding(true));
            StatusText = $"Relatório salvo em {Path.GetFileName(path)}.";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao salvar o relatório ({exception.GetType().Name}): {exception.Message}";
        }
    }

    /// <summary>What the last export sent, so an import in the same session sees the exact drafts that went out.</summary>
    private readonly Dictionary<int, BundleItem> _sessionExports = [];

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string([.. name.Select(character => invalid.Contains(character) ? '_' : character)]);
    }

    [RelayCommand]
    private async Task SaveCustomInstructionsAsync()
    {
        if (_project is null)
        {
            return;
        }

        try
        {
            await projectService.UpdateCustomInstructionsAsync(_project.Id, CustomInstructions);
            StatusText = "Instruções de tradução salvas.";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao salvar instruções ({exception.GetType().Name}): {exception.Message}";
        }
    }

    /// <summary>What the next bulk run would cost, in strings, characters and requests per engine.</summary>
    [ObservableProperty]
    public partial string LastPreview { get; set; } = string.Empty;

    /// <summary>
    /// Computes the split without translating anything, so the user can see how much of the mod is
    /// about to go to a paid engine before committing to the run.
    /// </summary>
    [RelayCommand]
    private async Task EstimateAsync()
    {
        if (_project is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await LoadPipelineAsync();

            if (_pipeline.PrimaryProfileId is not int primaryId)
            {
                StatusText = "Nenhum motor principal definido. Configure isso em Configurações.";
                return;
            }

            var plan = await BuildRoutingPlanAsync(primaryId, CancellationToken.None);
            if (plan is null)
            {
                return;
            }

            var pending = await projectStringService.GetPendingStringsAsync(_project.Id);
            if (pending.Count == 0)
            {
                StatusText = "Nenhuma string pendente de tradução.";
                LastPreview = string.Empty;
                return;
            }

            var glossaryTerms = await glossaryService.GetTermsAsync(_project.Id);
            LastPreview = DescribePreview(multiEngineTranslator.Preview(BuildRoutableItems(pending, glossaryTerms), plan));
            StatusText = $"Estimativa para {pending.Count:N0} string(s) pendente(s).";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao estimar ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Turns pending entries into routable ones, attaching what the app already knows about each: a
    /// string that previously failed review, or one a glossary term applies to, is worth a better engine
    /// however simple its text looks.
    /// </summary>
    private List<RoutableItem> BuildRoutableItems(IReadOnlyList<ProjectStringEntry> pending, IReadOnlyList<GlossaryTerm> glossaryTerms)
    {
        var previouslyFlagged = _allRows
            .Where(row => row.WasEverFlaggedForReview)
            .Select(row => row.Id)
            .ToHashSet();

        // Every label already translated somewhere in this project is a label the app can explain: the
        // engine has produced Portuguese for those words before, so they are not the ones it gives up
        // on. Read from the rows rather than from translation memory to keep this a lookup over data
        // already in hand - the routing plan is built for tens of thousands of strings at once.
        var knownLabels = _allRows
            .Where(row => !string.IsNullOrWhiteSpace(row.Translation) && StringDifficulty.IsBareLabel(row.Original))
            .Select(row => row.Original.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. pending.Select(entry => new RoutableItem(
                new TranslationSourceItem(entry.Id.ToString(CultureInfo.InvariantCulture), entry.OriginalText),
                new StringRiskContext(
                    PreviouslyFlaggedForReview: previouslyFlagged.Contains(entry.Id),
                    HasGlossaryTerm: glossaryTerms.Any(term => term.AppearsIn(entry.OriginalText)),

                    // A bare label the glossary does not cover and this project has never translated.
                    // That combination, not shortness, is what the weak engine chokes on.
                    IsUncoveredLabel: StringDifficulty.IsBareLabel(entry.OriginalText)
                        && !glossaryTerms.Any(term => term.AppearsIn(entry.OriginalText))
                        && !knownLabels.Contains(entry.OriginalText.Trim())))),
        ];
    }

    /// <summary>Builds the lane plan from the pipeline settings, or null (after setting a status) if an engine is missing.</summary>
    private async Task<RoutingPlan?> BuildRoutingPlanAsync(int primaryId, CancellationToken cancellationToken)
    {
        var primaryProvider = await providerFactory.CreateForProfileAsync(primaryId, reasoningOverride: null, cancellationToken);
        if (primaryProvider is null)
        {
            StatusText = "O perfil de tradução principal não existe mais. Revise Configurações.";
            return null;
        }

        // Lane zero always exists and catches everything no other lane claims.
        var lanes = new List<EngineLane> { new(NameOf(primaryId) ?? primaryProvider.Name, primaryProvider, 0) };

        if (_pipeline.IsTieredRoutingUsable)
        {
            foreach (var (profileId, minimumScore) in new[]
                     {
                         (_pipeline.MediumProfileId, _pipeline.MediumThreshold),
                         (_pipeline.DifficultProfileId, _pipeline.DifficultThreshold),
                     })
            {
                if (profileId is not int id || id == primaryId)
                {
                    continue;
                }

                var provider = await providerFactory.CreateForProfileAsync(id, reasoningOverride: null, cancellationToken);
                if (provider is null)
                {
                    StatusText = "Um dos motores de roteamento não existe mais. Revise Configurações.";
                    return null;
                }

                lanes.Add(new EngineLane(NameOf(id) ?? provider.Name, provider, minimumScore));
            }
        }

        return new RoutingPlan(lanes);
    }

    private static string DescribePreview(RoutingPreview preview)
    {
        if (preview.Lanes.Count == 0)
        {
            return string.Empty;
        }

        var parts = preview.Lanes.Select(lane =>
            $"{lane.Name}: {lane.ItemCount:N0} string(s), {lane.CharacterCount:N0} caracteres, ~{lane.EstimatedRequests:N0} requisição(ões)");

        // Characters and requests, not money: a monetary figure would need a per-model price table that
        // goes stale the moment a provider changes pricing. These are the units the bills are computed
        // from, and they are exact.
        return string.Join("  |  ", parts);
    }

    [RelayCommand]
    private Task TranslateAsync() => RunTranslationAsync(ReuseTranslationMemory);

    private async Task RunTranslationAsync(bool reuseTranslationMemory)
    {
        if (_project is null)
        {
            return;
        }

        _translateCts = new CancellationTokenSource();
        var cancellationToken = _translateCts.Token;

        var succeeded = 0;
        var needsReview = 0;
        var failed = 0;
        var reused = 0;

        try
        {
            IsBusy = true;
            IsTranslating = true;
            StatusText = "Verificando motores configurados...";

            // Re-read every run: the user may have changed roles in Settings since the page loaded.
            await LoadPipelineAsync();

            var primaryId = _pipeline.PrimaryProfileId;
            if (primaryId is null)
            {
                StatusText = "Nenhum motor principal definido. Vá em Configurações, adicione um perfil e escolha-o em \"Tradução principal\".";
                return;
            }

            var plan = await BuildRoutingPlanAsync(primaryId.Value, cancellationToken);
            if (plan is null)
            {
                return;
            }

            var pending = await projectStringService.GetPendingStringsAsync(_project.Id, cancellationToken);
            if (pending.Count == 0)
            {
                StatusText = "Nenhuma string pendente de tradução.";
                return;
            }

            var glossaryTerms = await glossaryService.GetTermsAsync(_project.Id, cancellationToken);
            var items = BuildRoutableItems(pending, glossaryTerms);

            // The preview is the cost estimate the user confirmed (or is about to see in the status
            // line): it is computed from the same plan the run uses, so the two can never disagree.
            var preview = multiEngineTranslator.Preview(items, plan);
            LastPreview = DescribePreview(preview);

            ProgressTotal = pending.Count;
            ProgressCurrent = 0;

            var engineLine = preview.Lanes.Count == 1
                ? $"Traduzindo {pending.Count} string(s) com {preview.Lanes[0].Name}"
                : "Traduzindo em paralelo: " + string.Join(", ", preview.Lanes.Select(lane => $"{lane.ItemCount} com {lane.Name}"));

            if (!reuseTranslationMemory)
            {
                engineLine += " (sem reaproveitar a memória)";
            }

            // Chunk size is scaled by the primary engine's own recommended concurrency, so a local
            // server that likes two queues still gets them.
            var checkpointSize = TranslationChunkSize * Math.Clamp(plan.Lanes[^1].Provider.Capabilities.RecommendedConcurrency, 1, 8);

            await multiEngineTranslator.TranslateAsync(
                items,
                plan,
                _project.SourceLocale,
                _project.TargetLocale,
                glossaryTerms,
                _project.CustomInstructions,
                checkpointSize,
                async (outcomes, laneName, ct) =>
                {
                    var updates = new List<ProjectStringTranslationUpdate>();
                    foreach (var outcome in outcomes)
                    {
                        var stringId = int.Parse(outcome.Id, CultureInfo.InvariantCulture);
                        if (outcome.TranslatedText is null)
                        {
                            failed++;
                            continue;
                        }

                        if (outcome.ReusedFromMemory)
                        {
                            reused++;
                        }

                        var status = outcome.Success ? ProjectStringStatus.Translated : ProjectStringStatus.NeedsReview;
                        var issueSummary = ValidationIssueText.Summarize(outcome.Issues);
                        updates.Add(new ProjectStringTranslationUpdate(stringId, outcome.TranslatedText, status, issueSummary));
                    }

                    // A checkpoint is atomic: only update the visible rows after the whole chunk is
                    // safely persisted. This part stays on the background thread - it is the slow bit.
                    await projectStringService.UpdateTranslationsAsync(updates, ct);

                    // Everything below is observed by XAML and must run on the UI thread; see _dispatcher.
                    await RunOnUiAsync(() =>
                    {
                        foreach (var update in updates)
                        {
                            var row = _allRows.FirstOrDefault(r => r.Id == update.StringEntryId);
                            row?.ApplyResult(update.TranslatedText, update.Status, update.IssueSummary);

                            if (update.Status == ProjectStringStatus.Translated)
                            {
                                succeeded++;
                            }
                            else
                            {
                                needsReview++;
                            }
                        }

                        ProgressCurrent += outcomes.Count;
                        StatusText = $"{engineLine}... {ProgressCurrent}/{ProgressTotal} ({succeeded} ok, {needsReview} revisar, {failed} sem resposta)";
                        ApplyFilter();
                    });
                },
                reuseTranslationMemory,
                cancellationToken);

            // The reuse count is stated out loud because it used to be invisible: a run that answered
            // every string out of memory and never called an engine reported exactly the same
            // "N traduzidas" as one that did all the work, which is how a discarded translation kept
            // coming back looking freshly made.
            StatusText = $"Tradução concluída: {succeeded} traduzida(s), {needsReview} para revisar, {failed} sem resposta do motor."
                + (reused > 0 ? $" {reused} vieram da memória de tradução, sem passar por nenhum motor." : string.Empty);
        }
        catch (OperationCanceledException)
        {
            StatusText = $"Tradução cancelada em {ProgressCurrent}/{ProgressTotal} - o que já foi traduzido está salvo ({succeeded} traduzida(s), {needsReview} revisar, {failed} sem resposta).";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao traduzir ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            IsTranslating = false;
            _translateCts?.Dispose();
            _translateCts = null;
        }
    }

    /// <summary>How many rows a restart would discard, and how many of those the user typed themselves.</summary>
    public (int Total, int ManuallyEdited) CountRestartImpact()
    {
        var translated = _allRows.Where(row => !string.IsNullOrEmpty(row.Translation)).ToList();
        return (translated.Count, translated.Count(row => row.Status is ProjectStringStatus.ManuallyEdited or ProjectStringStatus.Refined));
    }

    /// <summary>
    /// Throws this project's translations away and translates every string again, from nothing.
    /// </summary>
    /// <remarks>
    /// The missing answer to "this run came out badly, let me try another model". Everything the app had
    /// for doing that over worked against it: reverting the lines puts them back to pending, but the next
    /// run answers most of them straight out of translation memory, with the discarded text — and even
    /// deleting the whole project from Home does not help, because the memory is keyed by source text and
    /// survives the project that filled it. The result was an app that looked like it was re-translating
    /// and was in fact replaying a translation the user had already rejected.
    ///
    /// So this does all three parts in one gesture: clear the translations (kept as revisions), forget
    /// what they taught the memory for this locale pair, and run the pipeline with reuse switched off so
    /// nothing else can answer for the engine.
    /// </remarks>
    /// <param name="keepManualEdits">
    /// Leaves lines a human typed or refined untouched. On by default in the UI: those are the most
    /// expensive lines in the project and they are rarely what a bad machine run damaged.
    /// </param>
    public async Task RestartTranslationAsync(bool keepManualEdits)
    {
        if (_project is null || IsTranslating)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = "Descartando as traduções anteriores...";

            var doomed = _allRows
                .Where(row => !string.IsNullOrEmpty(row.Translation))
                .Where(row => !keepManualEdits || row.Status is not (ProjectStringStatus.ManuallyEdited or ProjectStringStatus.Refined))
                .ToList();

            if (doomed.Count == 0)
            {
                StatusText = "Não há tradução para descartar - o projeto já está todo pendente.";
                return;
            }

            var cleared = await projectStringService.ClearTranslationsAsync([.. doomed.Select(row => row.Id)]);

            foreach (var row in doomed)
            {
                row.ApplyResult(null, ProjectStringStatus.Pending, issueSummary: null);
            }

            // Without this the memory would still hold every discarded line, and the very next ordinary
            // "Traduzir pendentes" (which does reuse it) would put them all back.
            var forgotten = await translationMemory.DeleteManyAsync(
                [.. doomed.Select(row => row.Original)], _project.SourceLocale, _project.TargetLocale);

            ApplyFilter();
            StatusText = $"{cleared:N0} tradução(ões) descartada(s) e {forgotten:N0} entrada(s) esquecida(s) na memória. Retraduzindo do zero...";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao reiniciar ({exception.GetType().Name}): {exception.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await RunTranslationAsync(reuseTranslationMemory: false);
    }

    [RelayCommand]
    private void CancelTranslate() => _translateCts?.Cancel();

    public void Dispose() => _translateCts?.Dispose();

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
            StatusText = "Preparando exportação...";

            var entries = await projectStringService.GetStringsAsync(_project.Id);
            var plan = StringTableExportPlan.Build(entries, _project.SourceLocale, TryGetLocale, key => stringTableService.WithLocale(key, _project.TargetLocale));

            if (plan.Packages.Count == 0)
            {
                StatusText = "Não há nada para exportar neste projeto.";
                return;
            }

            // One mod means one file and the familiar "save as" dialog; several mods can't be a single
            // save dialog, because each mod has to come out as its own .package - exporting twelve mods
            // into one merged file is what made a twelve-mod project look like only the first one was
            // ever translated.
            var outputs = plan.Packages.Count == 1
                ? await ResolveSingleOutputAsync(plan)
                : await ResolveFolderOutputsAsync(plan);

            if (outputs is null)
            {
                return;
            }

            var writtenPackages = 0;
            var writtenStrings = 0;
            var invalidPackages = new List<string>();

            foreach (var (package, outputPath) in outputs)
            {
                StatusText = $"Gravando {Path.GetFileName(outputPath)} ({writtenPackages + 1}/{outputs.Count})...";

                // FileSavePicker.PickSaveFileAsync() creates an empty 0-byte file the moment the user
                // confirms a name/location, before we ever write to it. WriteStringTablesAsync refuses
                // to overwrite an existing file (never silently clobber a package) - remove that stub
                // first so the check doesn't misfire against the picker's own placeholder.
                if (File.Exists(outputPath) && new FileInfo(outputPath).Length == 0)
                {
                    File.Delete(outputPath);
                }

                await packageService.WriteStringTablesAsync(outputPath, package.Tables);

                StatusText = $"Validando {Path.GetFileName(outputPath)} (reabrindo e conferindo cada string)...";
                if (!await ValidateExportAsync(outputPath, package.Tables))
                {
                    invalidPackages.Add(Path.GetFileName(outputPath));
                }

                writtenPackages++;
                writtenStrings += package.StringCount;
            }

            var excludedNote = plan.ExcludedStringCount > 0
                ? $" {plan.ExcludedStringCount} string(s) de outros idiomas do próprio mod foram ignoradas."
                : string.Empty;

            StatusText = invalidPackages.Count == 0
                ? $"Exportado e validado com sucesso: {writtenPackages} package(s), {plan.TableCount} tabela(s), {writtenStrings} string(s).{excludedNote}"
                : $"Exportado, mas a validação de round-trip encontrou diferenças em: {string.Join(", ", invalidPackages)}. Não confie nesses arquivos sem investigar.";
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

    private async Task<IReadOnlyList<(ExportPackagePlan Package, string OutputPath)>?> ResolveSingleOutputAsync(ExportPlan plan)
    {
        var package = plan.Packages[0];
        var baseName = package.SourceModName ?? _project!.Name;
        var outputPath = await pathPicker.PickSaveFileAsync($"{baseName}_{_project!.TargetLocale.Name}.package");

        return outputPath is null ? null : [(package, outputPath)];
    }

    private async Task<IReadOnlyList<(ExportPackagePlan Package, string OutputPath)>?> ResolveFolderOutputsAsync(ExportPlan plan)
    {
        var folder = await pathPicker.PickFolderAsync();
        if (folder is null)
        {
            return null;
        }

        var outputs = new List<(ExportPackagePlan, string)>(plan.Packages.Count);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < plan.Packages.Count; index++)
        {
            var package = plan.Packages[index];
            var baseName = package.SourceModName ?? $"{_project!.Name}_sem-origem";
            var fileName = $"{baseName}_{_project!.TargetLocale.Name}.package";

            // Two mods in different folders can share a file name; silently writing both to the same
            // output path would mean the second one throws (the writer refuses to overwrite) after the
            // first already succeeded, leaving a half-finished export.
            if (!usedNames.Add(fileName))
            {
                fileName = $"{baseName}_{index + 1}_{_project.TargetLocale.Name}.package";
                usedNames.Add(fileName);
            }

            outputs.Add((package, Path.Combine(folder, fileName)));
        }

        return outputs;
    }

    /// <summary>
    /// The locale encoded in an STBL key, or <c>null</c> when its top instance byte is not a locale
    /// The Sims 4 knows about (an STBL a mod author built by hand with an arbitrary instance id).
    /// Such a table is not source text, so the export leaves it out rather than guessing.
    /// </summary>
    private CultureInfo? TryGetLocale(StblKey key)
    {
        try
        {
            return stringTableService.GetLocale(key);
        }
        catch (Exception exception) when (exception is NotSupportedException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private async Task<bool> ValidateExportAsync(string outputPath, IReadOnlyDictionary<StblKey, IReadOnlyList<StringTableEntry>> expectedTables)
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
