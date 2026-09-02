using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using TS4Translator.App.Services;
using TS4Translator.Core.Projects;
using TS4Translator.Core.Providers;
using TS4Translator.Core.Translation;
using TS4Translator.Providers.Argos;
using TS4Translator.Providers.LocalModel;

namespace TS4Translator.App.ViewModels;

/// <summary>
/// Backs Settings, which is three stacked concerns (brief section 41):
/// <list type="number">
/// <item><b>Motores</b> — the saved engine profiles. Several of the same kind may coexist.</item>
/// <item><b>Papéis</b> — which profile does the bulk pass, the per-row refinement, and the review sweep.</item>
/// <item><b>Roteamento</b> — optionally split the bulk pass by difficulty across up to three engines.</item>
/// </list>
/// Papéis and roteamento can be edited globally or for one project, chosen with a scope selector at the
/// top of those sections. They are deliberately separate from motores: an engine is configured once, a
/// role is a decision that changes often, and merging the two was what forced a reconfiguration every
/// time the user wanted a different engine.
/// </summary>
public sealed partial class SettingsPageViewModel(
    IProviderProfileService profileService,
    IProviderDirectory providerDirectory,
    ITranslationProviderFactory providerFactory,
    IProjectService projectService,
    IArgosRuntimeManager argosRuntimeManager,
    IPathPickerService pathPicker,
    ILocalModelRuntime localModelRuntime,
    LocalModelOptions localModelOptions) : ObservableObject
{
    /// <summary>Shown wherever a role may legitimately be left unset.</summary>
    public const string NoProfileOption = "(nenhum)";

    /// <summary>The scope selector's entry for the app-wide pipeline.</summary>
    public const string GlobalScopeOption = "Configuração global";

    public ObservableCollection<ProviderProfileRowViewModel> Profiles { get; } = [];

    public ObservableCollection<string> ProfileOptions { get; } = [];

    public ObservableCollection<string> ScopeOptions { get; } = [];

    public ObservableCollection<string> EditorModelOptions { get; } = [];

    public IReadOnlyList<string> ProviderKindOptions { get; } = ProviderKindLabels.All;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ArgosStatusText { get; set; } = "Verificando a instalação do Argos...";

    // ---- Profile editor ----

    [ObservableProperty]
    public partial bool IsEditingProfile { get; set; }

    [ObservableProperty]
    public partial string EditorTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditorName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditorKindLabel { get; set; } = ProviderKindLabels.Argos;

    [ObservableProperty]
    public partial string EditorEndpoint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditorModel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditorApiKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditorKindDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditorModelsStatus { get; set; } = string.Empty;

    /// <summary>Whether this engine thinks before answering — see <see cref="ReasoningMode"/>.</summary>
    [ObservableProperty]
    public partial string EditorReasoningLabel { get; set; } = ReasoningModeLabels.Off;

    public IReadOnlyList<string> ReasoningOptions { get; } = ReasoningModeLabels.All;

    /// <summary>Explains the selected choice, and whether this particular .gguf can honour it at all.</summary>
    public string EditorReasoningDescription
    {
        get
        {
            var description = ReasoningModeLabels.Describe(EditorReasoning);

            // Said plainly, because the alternative is a switch that silently does nothing: the mode is
            // a request to the model's own chat template, and most translation GGUFs have no thinking in
            // them to switch on. The file's header answers this without loading a single tensor.
            return _editorModelSupportsReasoning switch
            {
                true => description,
                false => $"{description}\n\nEste modelo .gguf não tem raciocínio - o template de chat dele não menciona <think>, então qualquer opção aqui dá no mesmo.",
                null => $"{description}\n\nEscolha um arquivo .gguf para o app conferir se ele tem raciocínio.",
            };
        }
    }

    private ReasoningMode EditorReasoning => ReasoningModeLabels.ToMode(EditorReasoningLabel);

    /// <summary>Null until a model file has been read; then whether its chat template deals in thinking.</summary>
    private bool? _editorModelSupportsReasoning;

    partial void OnEditorReasoningLabelChanged(string value) => OnPropertyChanged(nameof(EditorReasoningDescription));

    private int? _editingProfileId;

    /// <summary>Disambiguated display name per profile id — see <see cref="ReloadAsync"/>.</summary>
    private readonly Dictionary<int, string> _optionNamesById = [];

    private readonly Dictionary<string, Guid> _projectIdsByScopeName = [];

    public bool EditorShowsEndpoint => !ProviderKindLabels.NeedsNoConfiguration(EditorKind);

    public bool EditorShowsModel => ProviderKindLabels.HasModel(EditorKind);

    public bool EditorShowsApiKey => !ProviderKindLabels.NeedsNoConfiguration(EditorKind);

    public bool EditorShowsArgosPanel => EditorKind == ProviderKind.Argos;

    public bool EditorShowsLocalModelPanel => EditorKind == ProviderKind.LocalModel;

    /// <summary>Whether this engine can be asked which models the key may use. Only meaningful while the profile already exists.</summary>
    public bool EditorCanListModels => _editingProfileId is not null && providerDirectory.SupportsModelListing(EditorKind);

    private ProviderKind EditorKind => ProviderKindLabels.ToKind(EditorKindLabel);

    // ---- Roles and routing ----

    [ObservableProperty]
    public partial string SelectedScope { get; set; } = GlobalScopeOption;

    [ObservableProperty]
    public partial string PrimaryProfileName { get; set; } = NoProfileOption;

    [ObservableProperty]
    public partial string RefinementProfileName { get; set; } = NoProfileOption;

    [ObservableProperty]
    public partial string ReviewProfileName { get; set; } = NoProfileOption;

    [ObservableProperty]
    public partial bool TieredRoutingEnabled { get; set; }

    [ObservableProperty]
    public partial string MediumProfileName { get; set; } = NoProfileOption;

    [ObservableProperty]
    public partial double MediumThreshold { get; set; } = TranslationPipelineSettings.DefaultMediumThreshold;

    [ObservableProperty]
    public partial string DifficultProfileName { get; set; } = NoProfileOption;

    [ObservableProperty]
    public partial double DifficultThreshold { get; set; } = TranslationPipelineSettings.DefaultDifficultThreshold;

    /// <summary>Explains the current bands in plain language, recomputed as the sliders move.</summary>
    public string RoutingBandsSummary => TieredRoutingEnabled
        ? $"Até {MediumThreshold:F0}: {PrimaryProfileName}  ·  {MediumThreshold:F0} a {DifficultThreshold:F0}: {(MediumProfileName == NoProfileOption ? PrimaryProfileName : MediumProfileName)}  ·  {DifficultThreshold:F0}+: {(DifficultProfileName == NoProfileOption ? "—" : DifficultProfileName)}"
        : "Tudo vai para o motor principal.";

    public bool IsProjectScope => SelectedScope != GlobalScopeOption;

    partial void OnTieredRoutingEnabledChanged(bool value) => OnPropertyChanged(nameof(RoutingBandsSummary));

    partial void OnMediumThresholdChanged(double value)
    {
        // The bands have to stay ordered or the middle lane would swallow the difficult one.
        if (value > DifficultThreshold)
        {
            DifficultThreshold = value;
        }

        OnPropertyChanged(nameof(RoutingBandsSummary));
    }

    partial void OnDifficultThresholdChanged(double value)
    {
        if (value < MediumThreshold)
        {
            MediumThreshold = value;
        }

        OnPropertyChanged(nameof(RoutingBandsSummary));
    }

    partial void OnPrimaryProfileNameChanged(string value) => OnPropertyChanged(nameof(RoutingBandsSummary));

    partial void OnMediumProfileNameChanged(string value) => OnPropertyChanged(nameof(RoutingBandsSummary));

    partial void OnDifficultProfileNameChanged(string value) => OnPropertyChanged(nameof(RoutingBandsSummary));

    partial void OnSelectedScopeChanged(string value)
    {
        // Rebuilding ScopeOptions momentarily writes an empty selection back through the two-way
        // binding; treating that as a real scope change would reload the pipeline for a scope that does
        // not exist and blank the picker.
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        OnPropertyChanged(nameof(IsProjectScope));
        _ = LoadPipelineForScopeAsync();
    }

    partial void OnEditorKindLabelChanged(string value)
    {
        OnPropertyChanged(nameof(EditorShowsEndpoint));
        OnPropertyChanged(nameof(EditorShowsModel));
        OnPropertyChanged(nameof(EditorShowsApiKey));
        OnPropertyChanged(nameof(EditorShowsArgosPanel));
        OnPropertyChanged(nameof(EditorShowsLocalModelPanel));

        if (ProviderKindLabels.ToKind(value) == ProviderKind.LocalModel)
        {
            RefreshLocalModelStatus();
        }
        OnPropertyChanged(nameof(EditorCanListModels));

        var kind = ProviderKindLabels.ToKind(value);
        EditorKindDescription = ProviderKindLabels.Describe(kind);
        EditorModelOptions.Clear();
        EditorModelsStatus = string.Empty;

        // Only ever fills a field the user has not typed into, so switching kinds while exploring is
        // helpful but never destroys a real value.
        var (endpoint, model) = ProviderKindLabels.Defaults(kind);
        if (string.IsNullOrWhiteSpace(EditorEndpoint) || IsAKnownDefaultEndpoint(EditorEndpoint))
        {
            EditorEndpoint = endpoint;
        }

        if (string.IsNullOrWhiteSpace(EditorModel) || IsAKnownDefaultModel(EditorModel))
        {
            EditorModel = model;
        }

        if (string.IsNullOrWhiteSpace(EditorName) || ProviderKindLabels.All.Contains(EditorName))
        {
            EditorName = value;
        }
    }

    private static bool IsAKnownDefaultEndpoint(string endpoint) =>
        ProviderKindLabels.All.Any(label => string.Equals(ProviderKindLabels.Defaults(ProviderKindLabels.ToKind(label)).Endpoint, endpoint, StringComparison.OrdinalIgnoreCase));

    private static bool IsAKnownDefaultModel(string model) =>
        ProviderKindLabels.All.Any(label => string.Equals(ProviderKindLabels.Defaults(ProviderKindLabels.ToKind(label)).Model, model, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private async Task LoadAsync()
    {
        await RefreshArgosStatusAsync();
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var profiles = await profileService.ListAsync();

        Profiles.Clear();
        ProfileOptions.Clear();
        _optionNamesById.Clear();
        ProfileOptions.Add(NoProfileOption);

        // The role pickers are lists of strings, so their entries have to be unique or two profiles
        // sharing a name would be indistinguishable — and picking one would silently resolve to the
        // other. New saves reject duplicate names outright, but a database written before that rule
        // existed can still hold them, so the display name is disambiguated here rather than trusted.
        var duplicateNames = profiles
            .GroupBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        foreach (var profile in profiles)
        {
            var optionName = duplicateNames.Contains(profile.Name) ? $"{profile.Name} (#{profile.Id})" : profile.Name;
            _optionNamesById[profile.Id] = optionName;

            Profiles.Add(new ProviderProfileRowViewModel(profile));
            ProfileOptions.Add(optionName);
        }

        await ReloadScopesAsync();
        await LoadPipelineForScopeAsync();

        StatusText = profiles.Count == 0
            ? "Nenhum motor ainda. Comece adicionando o Argos (offline, sem configuração) ou o Google Tradutor."
            : $"{profiles.Count} motor(es) salvo(s).";
    }

    private async Task ReloadScopesAsync()
    {
        var projects = await projectService.ListRecentAsync();

        var previous = SelectedScope;
        ScopeOptions.Clear();
        _projectIdsByScopeName.Clear();
        ScopeOptions.Add(GlobalScopeOption);

        foreach (var project in projects)
        {
            // Project names are not guaranteed unique either; the id suffix keeps the picker honest.
            var scopeName = _projectIdsByScopeName.ContainsKey(project.Name)
                ? $"{project.Name} ({project.Id.ToString()[..8]})"
                : project.Name;

            _projectIdsByScopeName[scopeName] = project.Id;
            ScopeOptions.Add(scopeName);
        }

        SelectedScope = ScopeOptions.Contains(previous) ? previous : GlobalScopeOption;
    }

    private Guid? CurrentScopeProjectId =>
        _projectIdsByScopeName.TryGetValue(SelectedScope, out var projectId) ? projectId : null;

    private async Task LoadPipelineForScopeAsync()
    {
        var settings = await profileService.GetPipelineSettingsAsync(CurrentScopeProjectId);

        PrimaryProfileName = NameFor(settings.PrimaryProfileId);
        RefinementProfileName = NameFor(settings.RefinementProfileId);
        ReviewProfileName = NameFor(settings.ReviewProfileId);
        MediumProfileName = NameFor(settings.MediumProfileId);
        DifficultProfileName = NameFor(settings.DifficultProfileId);
        TieredRoutingEnabled = settings.TieredRoutingEnabled;
        MediumThreshold = settings.MediumThreshold;
        DifficultThreshold = settings.DifficultThreshold;

        AnnotateRoles(settings);

        // Forced, not incidental: rebuilding an ItemsSource resets the ComboBox's SelectedItem to null,
        // and CommunityToolkit only raises PropertyChanged when a value actually changes. A role whose
        // name happens to be the same as before therefore never told the control to re-select it, and
        // the box rendered blank while the setting behind it was fine.
        OnPropertyChanged(nameof(PrimaryProfileName));
        OnPropertyChanged(nameof(RefinementProfileName));
        OnPropertyChanged(nameof(ReviewProfileName));
        OnPropertyChanged(nameof(MediumProfileName));
        OnPropertyChanged(nameof(DifficultProfileName));
        OnPropertyChanged(nameof(SelectedScope));
        OnPropertyChanged(nameof(RoutingBandsSummary));
    }

    /// <summary>Labels each row with the jobs it holds, so the table alone answers "what is actually being used?".</summary>
    private void AnnotateRoles(TranslationPipelineSettings settings)
    {
        foreach (var row in Profiles)
        {
            var roles = new List<string>(5);
            if (settings.PrimaryProfileId == row.Id) { roles.Add("Principal"); }
            if (settings.RefinementProfileId == row.Id) { roles.Add("Refino"); }
            if (settings.ReviewProfileId == row.Id) { roles.Add("Revisão"); }
            if (settings.TieredRoutingEnabled && settings.MediumProfileId == row.Id) { roles.Add("Médias"); }
            if (settings.TieredRoutingEnabled && settings.DifficultProfileId == row.Id) { roles.Add("Difíceis"); }

            row.RolesText = string.Join(" · ", roles);
        }
    }

    private string NameFor(int? profileId) =>
        profileId is int id && _optionNamesById.TryGetValue(id, out var name) ? name : NoProfileOption;

    private int? IdFor(string optionName)
    {
        foreach (var (id, name) in _optionNamesById)
        {
            if (string.Equals(name, optionName, StringComparison.Ordinal))
            {
                return id;
            }
        }

        return null;
    }

    // ---- Profile commands ----

    [RelayCommand]
    private void AddProfile()
    {
        _editingProfileId = null;
        EditorTitle = "Novo motor";
        EditorKindLabel = ProviderKindLabels.Argos;
        EditorName = ProviderKindLabels.Argos;
        var (endpoint, model) = ProviderKindLabels.Defaults(ProviderKind.Argos);
        EditorEndpoint = endpoint;
        EditorModel = model;
        EditorApiKey = string.Empty;
        EditorKindDescription = ProviderKindLabels.Describe(ProviderKind.Argos);
        EditorModelOptions.Clear();
        EditorModelsStatus = string.Empty;
        EditorReasoningLabel = ReasoningModeLabels.Off;
        OnPropertyChanged(nameof(EditorCanListModels));
        IsEditingProfile = true;
    }

    public async Task EditProfileAsync(ProviderProfileRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var profile = await profileService.GetAsync(row.Id);
        if (profile is null)
        {
            return;
        }

        _editingProfileId = profile.Id;
        EditorTitle = $"Editando \"{profile.Name}\"";
        EditorKindLabel = ProviderKindLabels.ToLabel(profile.Kind);
        EditorName = profile.Name;
        EditorEndpoint = profile.Endpoint;
        EditorModel = profile.Model ?? string.Empty;
        EditorApiKey = string.Empty;
        EditorKindDescription = ProviderKindLabels.Describe(profile.Kind);
        EditorModelOptions.Clear();
        EditorModelsStatus = string.Empty;
        EditorReasoningLabel = ReasoningModeLabels.ToLabel(profile.Reasoning);
        OnPropertyChanged(nameof(EditorCanListModels));
        IsEditingProfile = true;

        if (profile.Kind == ProviderKind.LocalModel)
        {
            RefreshLocalModelStatus();
        }
    }

    [RelayCommand]
    private void CancelProfileEdit()
    {
        IsEditingProfile = false;
        _editingProfileId = null;
        EditorApiKey = string.Empty;
    }

    /// <summary>Asks the provider which models this key can actually use, instead of the user guessing an id.</summary>
    [RelayCommand]
    private async Task LoadModelsAsync()
    {
        if (_editingProfileId is not int profileId)
        {
            EditorModelsStatus = "Salve o motor primeiro - a lista vem da API usando a chave salva.";
            return;
        }

        try
        {
            IsBusy = true;
            EditorModelsStatus = "Consultando a API...";

            var models = await providerDirectory.ListModelsAsync(profileId);

            EditorModelOptions.Clear();
            foreach (var model in models)
            {
                EditorModelOptions.Add(model.Id);
            }

            EditorModelsStatus = models.Count == 0
                ? "A API não devolveu nenhum modelo utilizável."
                : $"{models.Count} modelo(s) disponíveis. Escolha um na lista ou continue digitando.";
        }
        catch (Exception exception)
        {
            EditorModelsStatus = $"Não foi possível listar ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        try
        {
            IsBusy = true;

            var kind = EditorKind;
            var isZeroConfiguration = ProviderKindLabels.NeedsNoConfiguration(kind);
            var modelRequired = ProviderKindLabels.HasModel(kind) && !isZeroConfiguration;

            if (string.IsNullOrWhiteSpace(EditorName))
            {
                StatusText = "Dê um nome ao motor - é assim que você vai escolhê-lo nos papéis abaixo.";
                return;
            }

            if (!isZeroConfiguration && string.IsNullOrWhiteSpace(EditorEndpoint))
            {
                StatusText = "Endpoint é obrigatório para este motor.";
                return;
            }

            if (modelRequired && string.IsNullOrWhiteSpace(EditorModel))
            {
                StatusText = "Modelo é obrigatório para este motor.";
                return;
            }

            // Zero-configuration engines supply their own endpoint instead of reading a field the UI
            // hides (and which could therefore hold something stale).
            var (defaultEndpoint, defaultModel) = ProviderKindLabels.Defaults(kind);
            var endpoint = isZeroConfiguration ? defaultEndpoint : EditorEndpoint;

            // The model is decided by whether the engine *has* one, never by whether it needs an
            // endpoint. Conflating the two silently discarded the .gguf path a user picked for
            // ProviderKind.LocalModel - the engine hides its endpoint and key, but that path is the only
            // setting it has, so blanking it kept the app on whatever model it happened to find first.
            var model = ProviderKindLabels.HasModel(kind)
                ? (string.IsNullOrWhiteSpace(EditorModel) ? null : EditorModel.Trim())
                : defaultModel;

            // Empty means "leave the stored key alone" - the box is never pre-filled with the real
            // secret, so an empty box after opening the editor just means the user didn't retype it.
            var apiKey = string.IsNullOrEmpty(EditorApiKey) ? null : EditorApiKey;

            var saved = await profileService.SaveAsync(
                _editingProfileId, EditorName, kind, endpoint, model, temperature: 0.0, isZeroConfiguration ? null : apiKey, EditorReasoning);

            // Kept open on a brand-new profile so "Buscar modelos" becomes usable immediately - it needs
            // a stored key, which only exists once the profile has been saved at least once.
            _editingProfileId = saved.Id;
            EditorTitle = $"Editando \"{saved.Name}\"";
            EditorApiKey = string.Empty;
            OnPropertyChanged(nameof(EditorCanListModels));

            await ReloadAsync();

            // Says which file the engine will actually load. A local model saved with no path falls back
            // to whatever .gguf the app can find, and the whole point of picking one is knowing that the
            // pick took - silence here is how a model swap looked done and was not.
            if (kind == ProviderKind.LocalModel)
            {
                RefreshLocalModelStatus();
                StatusText = $"Motor salvo. {LocalModelStatusText}";
                return;
            }

            StatusText = "Motor salvo.";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao salvar ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeleteProfileAsync(ProviderProfileRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        try
        {
            IsBusy = true;
            await profileService.DeleteAsync(row.Id);
            await ReloadAsync();
            StatusText = $"Motor \"{row.Name}\" removido.";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao remover ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Sends one real string through this engine, so "salvo" and "funciona" are never confused.</summary>
    public async Task TestProfileAsync(ProviderProfileRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        try
        {
            IsBusy = true;
            StatusText = $"Testando \"{row.Name}\"...";

            var provider = await providerFactory.CreateForProfileAsync(row.Id);
            if (provider is null)
            {
                StatusText = "Motor não encontrado.";
                return;
            }

            var results = await provider.TranslateBatchAsync(
                [new TranslationRequestItem("test", "Hello.")],
                CultureInfo.GetCultureInfo("en-US"),
                CultureInfo.GetCultureInfo("pt-BR"));

            var message = results.Count > 0
                ? $"\"{row.Name}\" OK. Resposta de teste: \"{results[0].TranslatedProtectedText}\""
                : $"\"{row.Name}\" respondeu, mas sem nenhum resultado utilizável. Verifique o modelo configurado.";

            // Quota comes from the same click when the provider reports it - DeepL is the only one of
            // these that exposes usage to an ordinary API key.
            var usage = await providerDirectory.GetUsageAsync(row.Id);
            if (usage is not null)
            {
                row.UsageText = usage.Description;
                message += $"  |  Cota: {usage.Description}";
            }

            StatusText = message;
        }
        catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Providers rename/retire model ids over time (Google retired "gemini-2.0-flash" in
            // June 2026). A wrong endpoint would usually fail at DNS/connection level, not with a
            // clean 404 from a server that did answer.
            StatusText = $"\"{row.Name}\" falhou com 404: o endpoint respondeu mas não encontrou o modelo. Use \"Buscar modelos\" ao editar para ver os ids válidos.";
        }
        catch (Exception exception)
        {
            StatusText = $"\"{row.Name}\" falhou ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- Pipeline commands ----

    [RelayCommand]
    private async Task SavePipelineAsync()
    {
        try
        {
            IsBusy = true;

            var primaryId = IdFor(PrimaryProfileName);
            var mediumId = IdFor(MediumProfileName);
            var difficultId = IdFor(DifficultProfileName);

            if (TieredRoutingEnabled && mediumId is null && difficultId is null)
            {
                StatusText = "Escolha ao menos um motor para as faixas médias ou difíceis, ou desligue o roteamento.";
                return;
            }

            var settings = new TranslationPipelineSettings(
                primaryId,
                IdFor(RefinementProfileName),
                IdFor(ReviewProfileName),
                TieredRoutingEnabled,
                mediumId,
                (int)Math.Round(MediumThreshold),
                difficultId,
                (int)Math.Round(DifficultThreshold)).Normalized();

            await profileService.SavePipelineSettingsAsync(settings, CurrentScopeProjectId);
            AnnotateRoles(settings);

            StatusText = IsProjectScope
                ? $"Configuração salva só para \"{SelectedScope}\"."
                : "Configuração global salva.";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao salvar ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Drops a project's override so it follows the global pipeline again.</summary>
    [RelayCommand]
    private async Task ResetScopeAsync()
    {
        if (CurrentScopeProjectId is not Guid projectId)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await profileService.RemovePipelineOverrideAsync(projectId);
            await LoadPipelineForScopeAsync();
            StatusText = $"\"{SelectedScope}\" voltou a seguir a configuração global.";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha ao redefinir ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task InstallArgosAsync()
    {
        try
        {
            IsBusy = true;
            var progress = new Progress<string>(message => ArgosStatusText = message);
            var status = await argosRuntimeManager.InstallAsync(progress);
            ArgosStatusText = status.Message;
        }
        catch (Exception exception)
        {
            ArgosStatusText = $"Falha na instalação ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshArgosStatusAsync()
    {
        var status = await argosRuntimeManager.GetStatusAsync();
        ArgosStatusText = status.Message;
    }

    // ---- Motor local do app (llama.cpp in-process) ----

    [ObservableProperty]
    public partial string LocalModelStatusText { get; set; } = string.Empty;

    /// <summary>
    /// Red when the engine would silently run on the CPU. That fallback is the single most misleading
    /// state this feature has: everything works, and it is several times slower than it should be.
    /// </summary>
    [ObservableProperty]
    public partial InfoBarSeverity LocalModelSeverity { get; set; } = InfoBarSeverity.Informational;

    [RelayCommand]
    private void RefreshLocalModelStatus()
    {
        var status = localModelRuntime.GetStatus(string.IsNullOrWhiteSpace(EditorModel) ? null : EditorModel);

        // Read out of the .gguf's own header, so the reasoning picker below can say whether it would do
        // anything for this file instead of leaving the user to find out over a whole mod.
        _editorModelSupportsReasoning = status.ModelPath is null ? null : status.SupportsReasoning;
        OnPropertyChanged(nameof(EditorReasoningDescription));

        LocalModelStatusText = status.Model is { } model
            ? $"{status.Message} {Describe(model)}"
            : status.Message;

        LocalModelSeverity = status switch
        {
            { IsReady: false } => InfoBarSeverity.Error,
            { IsCpuOnly: true } => InfoBarSeverity.Warning,
            _ => InfoBarSeverity.Success,
        };
    }

    private static string Describe(GgufModelInfo model)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(model.Name))
        {
            parts.Add(model.Name);
        }

        if (!string.IsNullOrWhiteSpace(model.Architecture))
        {
            parts.Add(model.Architecture);
        }

        parts.Add(model.SupportsReasoning ? "com raciocínio" : "sem raciocínio");
        return $"({string.Join(" · ", parts)})";
    }

    [RelayCommand]
    private async Task PickLocalModelAsync()
    {
        var path = await pathPicker.PickModelFileAsync();
        if (path is null)
        {
            return;
        }

        EditorModel = path;
        RefreshLocalModelStatus();
    }

    /// <summary>Opens the folder the engine reads from, so the user can drop a model or the runtime in.</summary>
    [RelayCommand]
    private void OpenLocalModelFolder()
    {
        try
        {
            Directory.CreateDirectory(localModelOptions.ModelsDirectory);
            Process.Start(new ProcessStartInfo(localModelOptions.RootDirectory) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            LocalModelStatusText = $"Não foi possível abrir a pasta ({exception.GetType().Name}): {exception.Message}";
        }
    }
}
