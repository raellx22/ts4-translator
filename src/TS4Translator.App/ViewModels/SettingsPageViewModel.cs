using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TS4Translator.Core.Providers;
using TS4Translator.Core.Translation;

namespace TS4Translator.App.ViewModels;

/// <summary>
/// Backs the Settings page's translation provider configuration (brief section 41). Only the
/// OpenAI-compatible provider (LM Studio, NVIDIA NIM, ...) is configurable today.
/// </summary>
public sealed partial class SettingsPageViewModel(
    IProviderConfigurationService configurationService,
    ITranslationProviderFactory providerFactory) : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = "LM Studio local";

    [ObservableProperty]
    public partial string Endpoint { get; set; } = "http://localhost:1234/v1";

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

        Name = config.Name;
        Endpoint = config.Endpoint;
        Model = config.Model;
        HasStoredApiKey = config.HasApiKey;
        StatusText = HasStoredApiKey ? "Configuração carregada (API key armazenada de forma segura)." : "Configuração carregada.";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            IsBusy = true;

            if (string.IsNullOrWhiteSpace(Endpoint) || string.IsNullOrWhiteSpace(Model))
            {
                StatusText = "Endpoint e Model são obrigatórios.";
                return;
            }

            // Empty ApiKey field means "leave whatever is stored untouched", not "clear it" - the
            // field is never pre-filled with the real key (it's a secret), so an empty box after
            // load just means the user didn't type a new one.
            var apiKeyToSave = string.IsNullOrEmpty(ApiKey) ? null : ApiKey;

            await configurationService.SaveAsync(Name, Endpoint, Model, temperature: 0.0, apiKeyToSave);
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
        catch (Exception exception)
        {
            StatusText = $"Falha na conexão ({exception.GetType().Name}): {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
