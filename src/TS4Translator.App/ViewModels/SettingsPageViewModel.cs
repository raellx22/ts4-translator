using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TS4Translator.Core.Providers;
using TS4Translator.Core.Translation;

namespace TS4Translator.App.ViewModels;

/// <summary>
/// Backs the Settings page's translation provider configuration (brief section 41). Three provider
/// kinds are configurable: OpenAI-compatible (LM Studio, NVIDIA NIM, or any other server speaking that
/// protocol), DeepL, and Google Gemini. Only one configuration is active at a time - switching
/// <see cref="SelectedProviderKindLabel"/> and saving replaces whichever was active before.
/// </summary>
public sealed partial class SettingsPageViewModel(
    IProviderConfigurationService configurationService,
    ITranslationProviderFactory providerFactory) : ObservableObject
{
    public const string OpenAICompatibleLabel = "OpenAI-compatible (LM Studio, NVIDIA NIM, ...)";
    public const string DeepLLabel = "DeepL";
    public const string GoogleGeminiLabel = "Google Gemini";

    public IReadOnlyList<string> ProviderKindOptions { get; } = [OpenAICompatibleLabel, DeepLLabel, GoogleGeminiLabel];

    [ObservableProperty]
    public partial string SelectedProviderKindLabel { get; set; } = OpenAICompatibleLabel;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    // Starts empty (not pre-filled with a default) so that OnSelectedProviderKindLabelChanged's
    // "only fill in a default when the field is still empty" rule actually fires the first time a
    // fresh install's user picks a provider kind - PlaceholderText below carries the LM Studio hint
    // in the meantime.
    [ObservableProperty]
    public partial string Endpoint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Model { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ApiKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasStoredApiKey { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>Whether the current selection uses a model id at all - DeepL does not.</summary>
    public bool ShowsModelField => SelectedProviderKindLabel != DeepLLabel;

    /// <summary>
    /// Every well-known default endpoint this page ever auto-fills (below, and the NVIDIA preset).
    /// Used to tell "still whatever a default put here" apart from "the user typed their own value" -
    /// switching the provider kind dropdown should keep auto-filling a sensible default across
    /// several kinds while the user is just exploring, but must stop the moment they've actually typed
    /// something real, and a plain "is it empty" check alone can't tell those two cases apart once a
    /// first default has already been filled in.
    /// </summary>
    private static readonly HashSet<string> KnownDefaultEndpoints = new(StringComparer.OrdinalIgnoreCase)
    {
        "http://localhost:1234/v1",
        "https://integrate.api.nvidia.com/v1",
        "https://api-free.deepl.com",
        "https://generativelanguage.googleapis.com",
    };

    partial void OnSelectedProviderKindLabelChanged(string value)
    {
        OnPropertyChanged(nameof(ShowsModelField));

        if (!string.IsNullOrWhiteSpace(Endpoint) && !KnownDefaultEndpoints.Contains(Endpoint))
        {
            return;
        }

        (Endpoint, Model) = value switch
        {
            DeepLLabel => ("https://api-free.deepl.com", string.Empty),
            GoogleGeminiLabel => ("https://generativelanguage.googleapis.com", "gemini-2.5-flash"),
            _ => ("http://localhost:1234/v1", Model),
        };
    }

    /// <summary>
    /// Fills endpoint/model for NVIDIA NIM's OpenAI-compatible chat-completions endpoint (brief
    /// section 5/17). Does not fill the API key (the user's own secret) or save automatically - the
    /// user still reviews and clicks "Salvar". The exact prompt format the Riva Translate model
    /// expects has not been validated against a real API key in this codebase (two sources disagreed
    /// during research - see docs/ROADMAP.md Fase 5); this preset only saves typing the well-known
    /// endpoint/model values, it does not claim the translation quality has been verified.
    /// </summary>
    [RelayCommand]
    private void ApplyNvidiaNimPreset()
    {
        SelectedProviderKindLabel = OpenAICompatibleLabel;
        Name = "NVIDIA NIM (Riva Translate)";
        Endpoint = "https://integrate.api.nvidia.com/v1";
        Model = "nvidia/riva-translate-4b-instruct-v2";
        StatusText = "Preset aplicado. Preencha sua API key da NVIDIA e clique em Salvar.";
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var config = await configurationService.GetActiveAsync();
        if (config is null)
        {
            StatusText = "Nenhum provider configurado ainda.";
            return;
        }

        SelectedProviderKindLabel = ToLabel(config.Kind);
        Name = config.Name;
        Endpoint = config.Endpoint;
        Model = config.Model ?? string.Empty;
        HasStoredApiKey = config.HasApiKey;
        StatusText = HasStoredApiKey ? "Configuração carregada (API key armazenada de forma segura)." : "Configuração carregada.";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            IsBusy = true;

            var kind = ToKind(SelectedProviderKindLabel);
            var modelRequired = kind != ProviderKind.DeepL;

            if (string.IsNullOrWhiteSpace(Endpoint) || (modelRequired && string.IsNullOrWhiteSpace(Model)))
            {
                StatusText = modelRequired ? "Endpoint e Model são obrigatórios." : "Endpoint é obrigatório.";
                return;
            }

            // Empty ApiKey field means "leave whatever is stored untouched", not "clear it" - the
            // field is never pre-filled with the real key (it's a secret), so an empty box after
            // load just means the user didn't type a new one.
            var apiKeyToSave = string.IsNullOrEmpty(ApiKey) ? null : ApiKey;
            var modelToSave = modelRequired ? Model : null;

            await configurationService.SaveAsync(kind, Name, Endpoint, modelToSave, temperature: 0.0, apiKeyToSave);
            HasStoredApiKey = HasStoredApiKey || !string.IsNullOrEmpty(ApiKey);
            ApiKey = string.Empty;
            StatusText = "Configuração salva.";
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

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        try
        {
            IsBusy = true;
            StatusText = "Testando conexão...";

            await SaveAsync();

            var provider = await providerFactory.CreateActiveProviderAsync();
            if (provider is null)
            {
                StatusText = "Configuração inválida - não foi possível criar o provider.";
                return;
            }

            var results = await provider.TranslateBatchAsync(
                [new TranslationRequestItem("test", "Hello.")],
                CultureInfo.GetCultureInfo("en-US"),
                CultureInfo.GetCultureInfo("pt-BR"));

            StatusText = results.Count > 0
                ? $"Conexão OK. Resposta de teste: \"{results[0].TranslatedProtectedText}\""
                : "O servidor respondeu, mas sem nenhum resultado utilizável. Verifique o modelo configurado.";
        }
        catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // The single most common cause seen in practice: providers rename/retire model ids over
            // time (e.g. Google retired "gemini-2.0-flash" in June 2026), and this app's hardcoded
            // default can go stale between updates. A wrong endpoint typo would usually surface as a
            // DNS/connection failure instead, not a clean 404 from a server that did respond.
            StatusText = $"Falha na conexão (404 Not Found): {exception.Message}\nO endpoint respondeu, mas não encontrou o modelo \"{Model}\" - confira se esse id ainda existe (providers costumam descontinuar modelos com o tempo) e corrija o campo Modelo.";
        }
        catch (Exception exception)
        {
            StatusText = $"Falha na conexão ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string ToLabel(ProviderKind kind) => kind switch
    {
        ProviderKind.DeepL => DeepLLabel,
        ProviderKind.GoogleGemini => GoogleGeminiLabel,
        _ => OpenAICompatibleLabel,
    };

    private static ProviderKind ToKind(string label) => label switch
    {
        DeepLLabel => ProviderKind.DeepL,
        GoogleGeminiLabel => ProviderKind.GoogleGemini,
        _ => ProviderKind.OpenAICompatible,
    };
}
